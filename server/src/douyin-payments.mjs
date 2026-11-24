import { randomBytes } from 'node:crypto';
import { OrderError, OrderService } from './orders.mjs';
import { SqliteOrderStore } from './sqlite-orders.mjs';
import { readDouyinPaymentWebhook } from './douyin-webhook.mjs';

const text = (value, max) => typeof value === 'string' && value.trim() === value && value.length > 0 &&
  value.length <= max && !/[\u0000-\u001f\u007f]/.test(value);

// Direct item purchase only. Server catalog is CNY cents; route determines platform tender.
export class DouyinPayments {
  constructor({ orders, catalog, route = 'Android', zoneId = '1' } = {}) {
    if (!(orders instanceof OrderService) || !(orders.store instanceof SqliteOrderStore) ||
        orders.binding.platform !== 'Douyin' || !['Android', 'IosCustomerService'].includes(route) ||
        !text(zoneId, 256) || !Array.isArray(catalog)) throw new TypeError('Invalid Douyin payment configuration');
    this.orders = orders; this.store = orders.store; this.route = route; this.zoneId = zoneId;
    this.products = new Map();
    for (const item of catalog) {
      const price = orders.catalog.get(item.productId);
      if (!price || price.currency !== 'CNY' || price.amountMinor > 2147483647 ||
          !text(item.productName, 10) || this.products.has(item.productId)) throw new TypeError('Invalid Douyin payment catalog');
      this.products.set(item.productId, Object.freeze({ ...price, productName: item.productName }));
    }
    this.store.transaction(() => {
      this.store.db.exec(`CREATE TABLE IF NOT EXISTS erdon_douyin_payment_schema(version INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS erdon_douyin_payments (
          order_id TEXT PRIMARY KEY, binding TEXT NOT NULL, subject TEXT NOT NULL,
          amount_minor INTEGER NOT NULL, route TEXT NOT NULL, zone_id TEXT NOT NULL,
          product_name TEXT NOT NULL, extra_info TEXT NOT NULL UNIQUE);`);
      const versions = this.store.db.prepare('SELECT version FROM erdon_douyin_payment_schema').all();
      if (versions.length > 1 || (versions.length && versions[0].version !== 1)) throw new OrderError('SERVER.SCHEMA_UNSUPPORTED', 503);
      if (!versions.length) this.store.db.exec('INSERT INTO erdon_douyin_payment_schema VALUES (1)');
    });
  }
  async prepare(session, orderId) {
    const owned = await this.orders.owned(session, orderId);
    return this.store.transaction(() => {
      const order = this.store.read(orderId);
      if (!order || order.binding !== this.orders.scope || order.subject !== owned.subject) throw new OrderError('PAY.ORDER_NOT_FOUND', 404);
      if (order.state !== 'Created') throw new OrderError('PAY.ORDER_ALREADY_PAID', 409);
      let row = this.store.db.prepare('SELECT * FROM erdon_douyin_payments WHERE order_id=?').get(orderId);
      if (!row) {
        const product = this.products.get(order.productId);
        if (!product || product.amountMinor !== order.amountMinor || order.currency !== 'CNY') throw new OrderError('PAY.PRODUCT_CHANGED', 409);
        if (this.store.db.prepare('SELECT COUNT(*) AS count FROM erdon_douyin_payments').get().count >= this.store.capacity)
          throw new OrderError('SERVER.CAPACITY', 503);
        this.store.db.prepare('INSERT INTO erdon_douyin_payments VALUES (?,?,?,?,?,?,?,?)')
          .run(orderId, order.binding, order.subject, order.amountMinor, this.route, this.zoneId, product.productName, randomBytes(32).toString('base64url'));
        row = this.store.db.prepare('SELECT * FROM erdon_douyin_payments WHERE order_id=?').get(orderId);
      }
      if (row.binding !== order.binding || row.subject !== order.subject || row.amount_minor !== order.amountMinor)
        throw new OrderError('PAY.VERIFICATION_MISMATCH', 409);
      return Object.freeze({ orderId, route: row.route, zoneId: row.zone_id, extraInfo: row.extra_info,
        amountMinor: row.amount_minor, productName: row.product_name, buyQuantity: 0, directPurchase: true });
    });
  }
  async acceptWebhook(envelope, callbackToken) {
    const checked = readDouyinPaymentWebhook(envelope, { callbackToken, appId: this.orders.binding.appId });
    if (!checked.valid) return Object.freeze({ accepted: false, code: checked.code });
    const event = checked.notification;
    return this.store.transaction(() => {
      const row = this.store.db.prepare('SELECT * FROM erdon_douyin_payments WHERE order_id=?').get(event.orderId);
      const order = this.store.read(event.orderId);
      if (!row || !order || row.binding !== this.orders.scope || order.binding !== this.orders.scope)
        throw new OrderError('PAY.PREPAYMENT_NOT_FOUND', 409);
      if (row.subject !== order.subject || event.extraInfo !== row.extra_info || event.amountMinor !== row.amount_minor ||
          order.amountMinor !== row.amount_minor || order.currency !== 'CNY' ||
          event.currency !== (row.route === 'Android' ? 'CNY' : 'DIAMOND')) throw new OrderError('PAY.VERIFICATION_MISMATCH', 409);
      const key = JSON.stringify(['Douyin', this.orders.binding.appId, event.transactionId]);
      const previous = this.store.db.prepare('SELECT order_id FROM erdon_orders WHERE transaction_key=?').get(key);
      if (previous && previous.order_id !== order.orderId) throw new OrderError('PAY.TRANSACTION_REUSED', 409);
      if (order.transactionKey && order.transactionKey !== key) throw new OrderError('PAY.TRANSACTION_CONFLICT', 409);
      const duplicate = order.state !== 'Created';
      if (!duplicate) this.store.db.prepare("UPDATE erdon_orders SET state='Paid',transaction_key=? WHERE order_id=?").run(key, order.orderId);
      return Object.freeze({ accepted: true, duplicate, state: duplicate ? order.state : 'Paid' });
    });
  }
}
