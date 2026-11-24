import { createHmac, randomBytes, timingSafeEqual } from 'node:crypto';
import { OrderError, OrderService } from './orders.mjs';
import { SqliteOrderStore } from './sqlite-orders.mjs';

export const wechatHmac = (key, value) => createHmac('sha256', key).update(value, 'utf8').digest('hex');
const text = (x, max = 256) => typeof x === 'string' && x.length > 0 && x.length <= max && x.trim() === x && !/[\u0000-\u001f\u007f]/.test(x);
const eventName = 'minigame_game_pay_goods_deliver_notify';

// WeChat mini-game virtual payment 2.0, Android direct goods only.
// Immutable preparation binds the 32-character platform order to our UUID order.
export class WeChatPayments {
  constructor({ orders, credentialStore, offerId, zoneId, appKey, isSandbox, originalId, queryClient = null } = {}) {
    if (!(orders instanceof OrderService) || !(orders.store instanceof SqliteOrderStore) || orders.binding.platform !== 'WeChat' ||
        typeof credentialStore?.get !== 'function' || ![offerId, zoneId, appKey, originalId].every(x => text(x)) || typeof isSandbox !== 'boolean')
      throw new TypeError('Invalid WeChat payment configuration');
    for (const item of orders.catalog.values()) if (item.currency !== 'CNY' || item.amountMinor > 2147483647) throw new TypeError('Invalid WeChat catalog');
    Object.assign(this, { orders, credentialStore, offerId, zoneId, appKey, originalId, queryClient });
    this.env = isSandbox ? 1 : 0; this.store = orders.store;
    this.store.transaction(() => {
      this.store.db.exec(`CREATE TABLE IF NOT EXISTS erdon_wechat_payment_schema(version INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS erdon_wechat_payments(order_id TEXT PRIMARY KEY, platform_order TEXT NOT NULL UNIQUE,
          binding TEXT NOT NULL, subject TEXT NOT NULL, sign_data TEXT NOT NULL);`);
      const rows = this.store.db.prepare('SELECT version FROM erdon_wechat_payment_schema').all();
      if (rows.length > 1 || (rows.length && rows[0].version !== 1)) throw new OrderError('SERVER.SCHEMA_UNSUPPORTED', 503);
      if (!rows.length) this.store.db.exec('INSERT INTO erdon_wechat_payment_schema VALUES(1)');
    });
  }
  async prepare(session, orderId) {
    const owned = await this.orders.owned(session, orderId);
    const credential = await this.credentialStore.get(this.orders.binding, owned.subject);
    if (!credential) throw new OrderError('PAY.SESSION_EXPIRED', 401);
    await this.orders.owned(session, orderId);
    const signData = this.store.transaction(() => {
      const order = this.store.read(orderId);
      if (order.state !== 'Created') throw new OrderError('PAY.ORDER_ALREADY_PAID', 409);
      let row = this.store.db.prepare('SELECT * FROM erdon_wechat_payments WHERE order_id=?').get(orderId);
      if (!row) {
        const product = this.orders.catalog.get(order.productId);
        if (!product || product.amountMinor !== order.amountMinor) throw new OrderError('PAY.PRODUCT_CHANGED', 409);
        if (this.store.db.prepare('SELECT COUNT(*) AS n FROM erdon_wechat_payments').get().n >= this.store.capacity) throw new OrderError('SERVER.CAPACITY', 503);
        const platformOrder = randomBytes(16).toString('hex');
        const value = JSON.stringify({ mode: 'goods', offerId: this.offerId, buyQuantity: 1, env: this.env, currencyType: 'CNY',
          platform: 'android', zoneId: this.zoneId, productId: order.productId, goodsPrice: order.amountMinor,
          outTradeNo: platformOrder, attach: randomBytes(32).toString('base64url') });
        this.store.db.prepare('INSERT INTO erdon_wechat_payments VALUES(?,?,?,?,?)').run(orderId, platformOrder, order.binding, order.subject, value);
        row = { binding: order.binding, subject: order.subject, sign_data: value };
      }
      if (row.binding !== order.binding || row.subject !== order.subject) throw new OrderError('PAY.VERIFICATION_MISMATCH', 409);
      const data = JSON.parse(row.sign_data);
      if (data.env !== this.env || data.offerId !== this.offerId || data.zoneId !== this.zoneId) throw new OrderError('PAY.CONFIG_CHANGED', 409);
      return row.sign_data;
    });
    return Object.freeze({ orderId, signData, paySignature: wechatHmac(this.appKey, 'requestMidasPaymentGameItem&' + signData),
      sessionSignature: wechatHmac(credential.sessionKey, signData) });
  }
  async query(session, orderId) {
    const order = await this.orders.owned(session, orderId);
    const row = this.store.db.prepare('SELECT * FROM erdon_wechat_payments WHERE order_id=?').get(orderId);
    if (!row || row.binding !== order.binding || row.subject !== order.subject) throw new OrderError('PAY.PREPAYMENT_NOT_FOUND', 404);
    if (!this.queryClient) throw new OrderError('PAY.QUERY_UNAVAILABLE', 503);
    const result = await this.queryClient.query(order.subject, JSON.parse(row.sign_data));
    const current = await this.orders.owned(session, orderId);
    return { orderId, platformStatus: result.status, state: current.state };
  }
  async acceptWebhook(envelope) {
    if (!envelope || envelope.ToUserName !== this.originalId || envelope.MsgType !== 'event' || envelope.Event !== eventName ||
        !Number.isSafeInteger(envelope.CreateTime) || envelope.CreateTime < 0 ||
        typeof envelope.MiniGame?.Payload !== 'string' || Buffer.byteLength(envelope.MiniGame.Payload) > 16384 ||
        !/^[a-fA-F0-9]{64}$/.test(envelope.MiniGame.PayEventSig ?? '')) throw new OrderError('WEBHOOK.INVALID', 400);
    const mini = envelope.MiniGame;
    const expected = Buffer.from(wechatHmac(this.appKey, eventName + '&' + mini.Payload), 'hex');
    if (!timingSafeEqual(expected, Buffer.from(mini.PayEventSig, 'hex'))) throw new OrderError('WEBHOOK.INVALID_SIGNATURE', 401);
    // MP's connectivity test must never mutate an order, including a real order ID.
    if (mini.IsMock === true) return { accepted: true, mock: true };
    if (mini.IsMock !== undefined && mini.IsMock !== false) throw new OrderError('WEBHOOK.INVALID', 400);
    let event;
    try { event = JSON.parse(mini.Payload); } catch { throw new OrderError('WEBHOOK.INVALID', 400); }
    return this.store.transaction(() => {
      if (!event || !text(event.OutTradeNo, 32)) throw new OrderError('WEBHOOK.INVALID', 400);
      const row = this.store.db.prepare('SELECT * FROM erdon_wechat_payments WHERE platform_order=?').get(event.OutTradeNo);
      if (!row || row.binding !== this.orders.scope) throw new OrderError('PAY.PREPAYMENT_NOT_FOUND', 409);
      const order = this.store.read(row.order_id), data = JSON.parse(row.sign_data), goods = event.GoodsInfo;
      if (!order || order.binding !== row.binding || order.subject !== row.subject || event.OpenId !== row.subject ||
          event.Env !== data.env || !goods || goods.ProductId !== order.productId || goods.Quantity !== data.buyQuantity ||
          goods.ZoneId !== data.zoneId || goods.OrigPrice !== order.amountMinor || goods.ActualPrice !== order.amountMinor ||
          goods.Attach !== data.attach || goods.OrderSource !== 1 || !text(event.WeChatPayInfo?.TransactionId))
        throw new OrderError('PAY.VERIFICATION_MISMATCH', 409);
      const key = JSON.stringify(['WeChat', this.orders.binding.appId, event.WeChatPayInfo.TransactionId]);
      const previous = this.store.db.prepare('SELECT order_id FROM erdon_orders WHERE transaction_key=?').get(key);
      if (previous && previous.order_id !== order.orderId) throw new OrderError('PAY.TRANSACTION_REUSED', 409);
      if (order.transactionKey && order.transactionKey !== key) throw new OrderError('PAY.TRANSACTION_CONFLICT', 409);
      if (order.state === 'Created') this.store.db.prepare("UPDATE erdon_orders SET state='Paid',transaction_key=? WHERE order_id=?").run(key, order.orderId);
      return { accepted: true, orderId: order.orderId, state: order.state === 'Created' ? 'Paid' : order.state };
    });
  }
}
