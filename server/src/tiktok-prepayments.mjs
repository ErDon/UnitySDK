import { OrderError } from './orders.mjs';
import { bindingKey } from './auth.mjs';
import { SqliteOrderStore } from './sqlite-orders.mjs';

const text = value => typeof value === 'string' && value.length > 0 && value.length <= 256 &&
  value.trim() === value && !/[\u0000-\u001f\u007f]/.test(value);
const columns = `app_id AS appId, trade_order_id AS tradeOrderId, order_id AS orderId,
  binding, subject, open_id AS openId, is_sandbox AS sandbox, token_amount AS tokenAmount`;

// Server-only repository. register must receive a trusted platform prepayment response,
// never IDs/prices supplied by the game client. Shares the order database transaction.
export class TikTokPrepayments {
  constructor(orderStore) {
    if (!(orderStore instanceof SqliteOrderStore)) throw new TypeError('SQLite order store required');
    this.store = orderStore;
    orderStore.transaction(() => {
      orderStore.db.exec('CREATE TABLE IF NOT EXISTS erdon_tiktok_prepayment_schema (version INTEGER NOT NULL)');
      const versions = orderStore.db.prepare('SELECT version FROM erdon_tiktok_prepayment_schema').all();
      if (versions.length > 1 || (versions.length === 1 && versions[0].version !== 1))
        throw new OrderError('SERVER.SCHEMA_UNSUPPORTED', 503);
      orderStore.db.exec(`CREATE TABLE IF NOT EXISTS erdon_tiktok_prepayments (
        app_id TEXT NOT NULL, trade_order_id TEXT NOT NULL, order_id TEXT NOT NULL,
        binding TEXT NOT NULL, subject TEXT NOT NULL, open_id TEXT NOT NULL,
        is_sandbox INTEGER NOT NULL CHECK(is_sandbox IN (0,1)), token_amount INTEGER NOT NULL CHECK(token_amount > 0),
        PRIMARY KEY(app_id, trade_order_id));
        CREATE INDEX IF NOT EXISTS erdon_tiktok_order ON erdon_tiktok_prepayments(order_id);`);
      if (versions.length === 0) orderStore.db.exec('INSERT INTO erdon_tiktok_prepayment_schema VALUES (1)');
    });
  }
  validate(binding, isSandbox) {
    if (!binding || binding.platform !== 'TikTok' ||
        !['gameId', 'appId', 'environment'].every(key => text(binding[key])) || typeof isSandbox !== 'boolean')
      throw new TypeError('Invalid TikTok prepayment binding');
  }
  async register({ binding, isSandbox, orderId, tradeOrderId, openId, tokenAmount }) {
    this.validate(binding, isSandbox);
    if (![orderId, tradeOrderId, openId].every(text) || !Number.isSafeInteger(tokenAmount) || tokenAmount <= 0)
      throw new TypeError('Invalid TikTok prepayment association');
    return this.store.transaction(() => {
      const order = this.store.read(orderId), scope = bindingKey(binding);
      if (!order || order.binding !== scope) throw new OrderError('PAY.ORDER_NOT_FOUND', 404);
      const existing = this.store.db.prepare(`SELECT ${columns} FROM erdon_tiktok_prepayments WHERE app_id=? AND trade_order_id=?`).get(binding.appId, tradeOrderId);
      if (existing) {
        if (existing.orderId !== orderId || existing.binding !== scope || existing.subject !== order.subject ||
            existing.openId !== openId || existing.sandbox !== Number(isSandbox) || existing.tokenAmount !== tokenAmount)
          throw new OrderError('PAY.PREPAYMENT_CONFLICT', 409);
        return this.snapshot(existing);
      }
      if (order.state !== 'Created') throw new OrderError('PAY.ORDER_ALREADY_PAID', 409);
      if (this.store.db.prepare('SELECT COUNT(*) AS count FROM erdon_tiktok_prepayments').get().count >= this.store.capacity)
        throw new OrderError('SERVER.CAPACITY', 503);
      this.store.db.prepare(`INSERT INTO erdon_tiktok_prepayments
        (app_id,trade_order_id,order_id,binding,subject,open_id,is_sandbox,token_amount) VALUES (?,?,?,?,?,?,?,?)`)
        .run(binding.appId, tradeOrderId, orderId, scope, order.subject, openId, Number(isSandbox), tokenAmount);
      return this.snapshot({ appId: binding.appId, tradeOrderId, orderId, binding: scope,
        subject: order.subject, openId, sandbox: Number(isSandbox), tokenAmount });
    });
  }
  async get({ binding, isSandbox, tradeOrderId }) {
    this.validate(binding, isSandbox);
    if (!text(tradeOrderId)) throw new TypeError('Invalid TikTok trade order ID');
    return this.store.transaction(() => {
      const row = this.store.db.prepare(`SELECT ${columns} FROM erdon_tiktok_prepayments WHERE app_id=? AND trade_order_id=?`).get(binding.appId, tradeOrderId);
      return row && row.binding === bindingKey(binding) && row.sandbox === Number(isSandbox) ? this.snapshot(row) : null;
    });
  }
  snapshot(row) {
    return Object.freeze({ appId: row.appId, tradeOrderId: row.tradeOrderId, orderId: row.orderId,
      binding: row.binding, subject: row.subject, openId: row.openId, isSandbox: row.sandbox === 1, tokenAmount: row.tokenAmount });
  }
}
