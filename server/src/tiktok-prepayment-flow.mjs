import { createHash } from 'node:crypto';
import { OrderError } from './orders.mjs';
import { bindingKey } from './auth.mjs';
import { TikTokPrepayments } from './tiktok-prepayments.mjs';

// Internal service composition, not a public client endpoint. All arguments must
// come from authenticated server identity/catalog configuration.
export class TikTokPrepaymentFlow {
  constructor(payments, tradeClient) {
    if (!(payments instanceof TikTokPrepayments) || typeof tradeClient?.create !== 'function')
      throw new TypeError('Invalid prepayment flow dependencies');
    this.payments = payments; this.client = tradeClient; this.store = payments.store;
    this.store.transaction(() => {
      this.store.db.exec('CREATE TABLE IF NOT EXISTS erdon_tiktok_attempt_schema (version INTEGER NOT NULL)');
      const versions = this.store.db.prepare('SELECT version FROM erdon_tiktok_attempt_schema').all();
      if (versions.length > 1 || (versions.length === 1 && versions[0].version !== 1)) throw new OrderError('SERVER.SCHEMA_UNSUPPORTED', 503);
      this.store.db.exec(`CREATE TABLE IF NOT EXISTS erdon_tiktok_attempts (
        order_id TEXT PRIMARY KEY, request_hash TEXT NOT NULL,
        state TEXT NOT NULL CHECK(state IN ('Pending','Ready')), trade_order_id TEXT,
        CHECK((state='Pending' AND trade_order_id IS NULL) OR (state='Ready' AND trade_order_id IS NOT NULL)));`);
      if (!versions.length) this.store.db.exec('INSERT INTO erdon_tiktok_attempt_schema VALUES (1)');
    });
  }
  async create({ binding, isSandbox, openId, accessToken, orderId, tokenAmount, productName, orderUrl, quantity, quantityUnit, imageUrl }) {
    this.payments.validate(binding, isSandbox);
    if (typeof openId !== 'string' || !openId.trim() || openId.length > 256 ||
        typeof accessToken !== 'string' || !accessToken || !Number.isSafeInteger(tokenAmount) || tokenAmount <= 0 ||
        !Number.isSafeInteger(quantity) || quantity <= 0 ||
        ![orderId, productName, orderUrl, quantityUnit, imageUrl].every(v => typeof v === 'string' && v.trim()))
      throw new TypeError('Invalid prepayment flow request');
    const scope = Object.freeze({ ...binding });
    const request = { accessToken, tokenAmount, orderId, productName, orderUrl, quantity, quantityUnit, imageUrl };
    const claim = this.store.transaction(() => {
      const order = this.store.read(orderId);
      if (!order || order.binding !== bindingKey(scope)) throw new OrderError('PAY.ORDER_NOT_FOUND', 404);
      if (order.state !== 'Created') throw new OrderError('PAY.ORDER_ALREADY_PAID', 409);
      request.productId = order.productId;
      const hash = createHash('sha256').update(JSON.stringify([bindingKey(scope), isSandbox, openId,
        tokenAmount, orderId, order.productId, productName, orderUrl, quantity, quantityUnit, imageUrl])).digest('hex');
      const existing = this.store.db.prepare('SELECT * FROM erdon_tiktok_attempts WHERE order_id=?').get(orderId);
      if (existing) {
        if (existing.request_hash !== hash) throw new OrderError('PAY.PREPAYMENT_CONFLICT', 409);
        if (existing.state === 'Pending') throw new OrderError('PAY.PREPAYMENT_UNCERTAIN', 409);
        return { tradeOrderId: existing.trade_order_id };
      }
      if (this.store.db.prepare('SELECT COUNT(*) AS count FROM erdon_tiktok_attempts').get().count >= this.store.capacity)
        throw new OrderError('SERVER.CAPACITY', 503);
      this.store.db.prepare("INSERT INTO erdon_tiktok_attempts(order_id,request_hash,state) VALUES (?,?,'Pending')").run(orderId, hash);
      return null;
    });
    if (claim) return Object.freeze(claim);
    // Pending remains durable on any failure/crash: no retry can silently issue another create.
    const result = await this.client.create(Object.freeze(request));
    await this.payments.register({ binding: scope, isSandbox, openId, orderId, tokenAmount, tradeOrderId: result.tradeOrderId });
    this.store.transaction(() => this.store.db.prepare("UPDATE erdon_tiktok_attempts SET state='Ready',trade_order_id=? WHERE order_id=? AND state='Pending'").run(result.tradeOrderId, orderId));
    return Object.freeze({ tradeOrderId: result.tradeOrderId });
  }
}
