import { DatabaseSync } from 'node:sqlite';
import { OrderError } from './orders.mjs';

const columns = `order_id AS orderId, binding, subject, product_id AS productId,
  amount_minor AS amountMinor, currency, state, created_at AS createdAt, transaction_key AS transactionKey`;
export class SqliteOrderStore {
  constructor(path, { capacity = 10000 } = {}) {
    if (typeof path !== 'string' || !path || !Number.isSafeInteger(capacity) || capacity <= 0)
      throw new TypeError('Invalid order store configuration');
    this.capacity = capacity; this.closed = false;
    this.db = new DatabaseSync(path);
    try {
      this.db.exec('PRAGMA busy_timeout=5000; PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; BEGIN IMMEDIATE;');
      this.db.exec('CREATE TABLE IF NOT EXISTS erdon_order_schema (version INTEGER NOT NULL)');
      const versions = this.db.prepare('SELECT version FROM erdon_order_schema').all();
      if (versions.length > 1 || (versions.length === 1 && versions[0].version !== 1)) throw new Error('Unsupported schema');
      this.db.exec(`CREATE TABLE IF NOT EXISTS erdon_orders (
        order_id TEXT PRIMARY KEY, request_key TEXT NOT NULL UNIQUE, binding TEXT NOT NULL, subject TEXT NOT NULL,
        product_id TEXT NOT NULL, amount_minor INTEGER NOT NULL CHECK(amount_minor > 0), currency TEXT NOT NULL,
        state TEXT NOT NULL CHECK(state IN ('Created','Paid','Fulfilled')), created_at INTEGER NOT NULL,
        transaction_key TEXT UNIQUE,
        CHECK((state = 'Created' AND transaction_key IS NULL) OR (state <> 'Created' AND transaction_key IS NOT NULL)));
        CREATE INDEX IF NOT EXISTS erdon_order_pending ON erdon_orders(state,created_at);`);
      if (versions.length === 0) this.db.exec('INSERT INTO erdon_order_schema VALUES (1)');
      this.db.exec('COMMIT');
    } catch {
      try { this.db.exec('ROLLBACK'); } catch { }
      this.db.close(); this.closed = true;
      throw new Error('Cannot initialize order database; verify path, permissions and schema version');
    }
  }
  transaction(action) {
    if (this.closed) throw new OrderError('SERVER.STORAGE_UNAVAILABLE', 503);
    try {
      this.db.exec('BEGIN IMMEDIATE');const result = action();this.db.exec('COMMIT');return result;
    } catch (error) {
      try { this.db.exec('ROLLBACK'); } catch { }
      if (error instanceof OrderError) throw error;
      throw new OrderError('SERVER.STORAGE_UNAVAILABLE', 503);
    }
  }
  read(orderId) { return this.db.prepare(`SELECT ${columns} FROM erdon_orders WHERE order_id=?`).get(orderId); }
  async create(requestKey, order) {
    return this.transaction(() => {
      const existing = this.db.prepare(`SELECT ${columns} FROM erdon_orders WHERE request_key=?`).get(requestKey);
      if (existing) {
        if (existing.productId !== order.productId) throw new OrderError('PAY.IDEMPOTENCY_CONFLICT', 409);
        return { ...existing };
      }
      if (this.db.prepare('SELECT COUNT(*) AS count FROM erdon_orders').get().count >= this.capacity)
        throw new OrderError('SERVER.CAPACITY', 503);
      this.db.prepare(`INSERT INTO erdon_orders
        (order_id,request_key,binding,subject,product_id,amount_minor,currency,state,created_at)
        VALUES (?,?,?,?,?,?,?,'Created',?)`).run(order.orderId,requestKey,order.binding,order.subject,order.productId,order.amountMinor,order.currency,order.createdAt);
      return { ...this.read(order.orderId) };
    });
  }
  async get(orderId) { return this.transaction(() => { const row = this.read(orderId);return row ? { ...row } : null; }); }
  async markPaid(orderId, transactionKey) {
    return this.transaction(() => {
      const order = this.read(orderId);
      if (!order) throw new OrderError('PAY.ORDER_NOT_FOUND', 404);
      const owner = this.db.prepare('SELECT order_id FROM erdon_orders WHERE transaction_key=?').get(transactionKey);
      if (owner && owner.order_id !== orderId) throw new OrderError('PAY.TRANSACTION_REUSED', 409);
      if (order.transactionKey && order.transactionKey !== transactionKey) throw new OrderError('PAY.TRANSACTION_CONFLICT', 409);
      if (order.state === 'Created')
        this.db.prepare("UPDATE erdon_orders SET state='Paid',transaction_key=? WHERE order_id=?").run(transactionKey,orderId);
      return { ...this.read(orderId) };
    });
  }
  async markFulfilled(orderId) {
    return this.transaction(() => {
      const order = this.read(orderId);
      if (!order || !['Paid','Fulfilled'].includes(order.state)) throw new OrderError('PAY.NOT_PAID', 409);
      this.db.prepare("UPDATE erdon_orders SET state='Fulfilled' WHERE order_id=?").run(orderId);
      return { ...this.read(orderId) };
    });
  }
  async pendingDelivery() {
    return this.transaction(() => this.db.prepare(`SELECT ${columns} FROM erdon_orders WHERE state='Paid' ORDER BY created_at,order_id`).all().map(x=>({ ...x })));
  }
  close() { if (!this.closed) { this.db.close();this.closed = true; } }
}
