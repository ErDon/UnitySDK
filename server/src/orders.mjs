import { randomUUID } from 'node:crypto';
import { bindingKey } from './auth.mjs';

export class OrderError extends Error {
  constructor(code, status = 400) { super(code); this.code = code; this.status = status; }
}
const validText = value => typeof value === 'string' && value.trim().length > 0 && value.length <= 256;
const publicOrder = order => ({ orderId: order.orderId, productId: order.productId,
  amountMinor: order.amountMinor, currency: order.currency, state: order.state, createdAt: order.createdAt });

// Local reference only. Production requires a transactional durable store with the same atomic semantics.
export class MemoryOrderStore {
  constructor({ capacity = 10000 } = {}) {
    if (!Number.isSafeInteger(capacity) || capacity <= 0) throw new TypeError('Invalid order capacity');
    this.capacity = capacity; this.orders = new Map(); this.requests = new Map(); this.transactions = new Map();
  }
  async create(requestKey, order) {
    const existing = this.orders.get(this.requests.get(requestKey));
    if (existing) {
      if (existing.productId !== order.productId) throw new OrderError('PAY.IDEMPOTENCY_CONFLICT', 409);
      return { ...existing };
    }
    if (this.orders.size >= this.capacity) throw new OrderError('SERVER.CAPACITY', 503);
    this.orders.set(order.orderId, { ...order }); this.requests.set(requestKey, order.orderId);
    return { ...order };
  }
  async get(orderId) { const value = this.orders.get(orderId); return value ? { ...value } : null; }
  async markPaid(orderId, transactionKey) {
    const order = this.orders.get(orderId);
    if (!order) throw new OrderError('PAY.ORDER_NOT_FOUND', 404);
    const owner = this.transactions.get(transactionKey);
    if (owner && owner !== orderId) throw new OrderError('PAY.TRANSACTION_REUSED', 409);
    if (order.transactionKey && order.transactionKey !== transactionKey) throw new OrderError('PAY.TRANSACTION_CONFLICT', 409);
    if (order.state === 'Created') {
      this.transactions.set(transactionKey, orderId); order.transactionKey = transactionKey; order.state = 'Paid';
    }
    return { ...order };
  }
  async markFulfilled(orderId) {
    const order = this.orders.get(orderId);
    if (!order || !['Paid', 'Fulfilled'].includes(order.state)) throw new OrderError('PAY.NOT_PAID', 409);
    order.state = 'Fulfilled'; return { ...order };
  }
  async pendingDelivery() { return [...this.orders.values()].filter(x => x.state === 'Paid').map(x => ({ ...x })); }
}

export class OrderService {
  constructor({ binding, catalog, auth, store, verifyPayment, now = Date.now }) {
    if (!binding || !['gameId', 'appId', 'environment', 'platform'].every(k => validText(binding[k])) ||
        !auth || !store || typeof verifyPayment !== 'function' || !Array.isArray(catalog))
      throw new TypeError('Invalid order service configuration');
    this.binding = Object.freeze({ gameId: binding.gameId, appId: binding.appId, environment: binding.environment, platform: binding.platform });
    this.scope = bindingKey(this.binding); this.auth = auth; this.store = store;
    this.verifyPayment = verifyPayment; this.now = now; this.delivering = new Set(); this.catalog = new Map();
    for (const product of catalog) {
      if (!validText(product.productId) || !Number.isSafeInteger(product.amountMinor) || product.amountMinor <= 0 ||
          !/^[A-Z]{3}$/.test(product.currency) || this.catalog.has(product.productId)) throw new TypeError('Invalid product catalog');
      this.catalog.set(product.productId, Object.freeze({ productId: product.productId, amountMinor: product.amountMinor, currency: product.currency }));
    }
  }
  async create(session, { productId, requestId } = {}) {
    const identity = await this.auth.authenticate(session, this.binding);
    if (!validText(requestId) || !validText(productId)) throw new OrderError('PAY.INVALID_REQUEST');
    const product = this.catalog.get(productId);
    if (!product) throw new OrderError('PAY.PRODUCT_NOT_FOUND', 404);
    const order = await this.store.create(JSON.stringify([this.scope, identity.subject, requestId]), {
      orderId: randomUUID(), binding: this.scope, subject: identity.subject, ...product, state: 'Created', createdAt: this.now()
    });
    return publicOrder(order);
  }
  async owned(session, orderId) {
    const identity = await this.auth.authenticate(session, this.binding);
    const order = await this.store.get(orderId);
    if (!order || order.binding !== this.scope || order.subject !== identity.subject) throw new OrderError('PAY.ORDER_NOT_FOUND', 404);
    return order;
  }
  async get(session, orderId) { return publicOrder(await this.owned(session, orderId)); }
  async confirm(session, orderId, evidence) {
    const order = await this.owned(session, orderId);
    if (order.state !== 'Created') return publicOrder(order);
    let payment;
    try { payment = await this.verifyPayment(this.binding, Object.freeze({ ...order }), evidence); }
    catch { throw new OrderError('PAY.VERIFICATION_FAILED', 502); }
    if (!payment || payment.paid !== true) throw new OrderError('PAY.NOT_PAID', 409);
    if (payment.orderId !== order.orderId || payment.appId !== this.binding.appId || payment.platform !== this.binding.platform ||
        payment.subject !== order.subject || payment.amountMinor !== order.amountMinor || payment.currency !== order.currency ||
        !validText(payment.transactionId)) throw new OrderError('PAY.VERIFICATION_MISMATCH', 409);
    // Platform transaction identity is application-scoped, not environment-scoped.
    const key = JSON.stringify([this.binding.platform, this.binding.appId, payment.transactionId]);
    return publicOrder(await this.store.markPaid(orderId, key));
  }
  // Server worker only. Receiver MUST atomically deduplicate deliveryKey alongside granting goods.
  async fulfill(orderId, deliver) {
    if (typeof deliver !== 'function') throw new TypeError('Missing delivery handler');
    if (this.delivering.has(orderId)) throw new OrderError('PAY.DELIVERY_IN_PROGRESS', 409);
    this.delivering.add(orderId);
    try {
      const order = await this.store.get(orderId);
      if (!order || order.binding !== this.scope) throw new OrderError('PAY.ORDER_NOT_FOUND', 404);
      if (order.state === 'Fulfilled') return publicOrder(order);
      if (order.state !== 'Paid') throw new OrderError('PAY.NOT_PAID', 409);
      let accepted;
      try { accepted = await deliver(Object.freeze({ deliveryKey: order.orderId, subject: order.subject,
        productId: order.productId, gameId: this.binding.gameId, environment: this.binding.environment })); }
      catch { throw new OrderError('PAY.DELIVERY_FAILED', 502); }
      if (accepted !== true) throw new OrderError('PAY.DELIVERY_FAILED', 502);
      return publicOrder(await this.store.markFulfilled(orderId));
    } finally { this.delivering.delete(orderId); }
  }
}
