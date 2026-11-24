import { OrderError, OrderService } from './orders.mjs';
import { bindingKey } from './auth.mjs';
import { TikTokPrepaymentFlow } from './tiktok-prepayment-flow.mjs';
import { validateTikTokTradeRequest } from './tiktok-trade-client.mjs';

export class TikTokPrepaymentService {
  constructor({ orders, flow, catalog, credentials, isSandbox, now = Date.now, orderUrlPrefix = '/orders/' }) {
    if (!(orders instanceof OrderService) || !(flow instanceof TikTokPrepaymentFlow) || orders.store !== flow.store ||
        orders.binding.platform !== 'TikTok' || typeof credentials?.get !== 'function' || !Array.isArray(catalog) ||
        typeof isSandbox !== 'boolean' || typeof now !== 'function' || typeof orderUrlPrefix !== 'string' ||
        !orderUrlPrefix.startsWith('/') || orderUrlPrefix.startsWith('//') || orderUrlPrefix.length > 1024 || /[\s?#]/.test(orderUrlPrefix))
      throw new TypeError('Invalid TikTok prepayment service configuration');
    this.orders = orders; this.flow = flow; this.credentials = credentials; this.binding = orders.binding;
    this.isSandbox = isSandbox; this.now = now; this.orderUrlPrefix = orderUrlPrefix; this.catalog = new Map();
    for (const product of catalog) {
      const item = { productId: product.productId, productName: product.productName, tokenAmount: product.tokenAmount,
        quantity: product.quantity, quantityUnit: product.quantityUnit, imageUrl: product.imageUrl };
      validateTikTokTradeRequest({ ...item, orderId: 'configuration-check', accessToken: 'configuration-check', orderUrl: orderUrlPrefix });
      if (!orders.catalog.has(item.productId) || this.catalog.has(item.productId)) throw new TypeError('Invalid TikTok prepayment catalog');
      this.catalog.set(item.productId, Object.freeze(item));
    }
  }
  async prepare(session, orderId) {
    // Ownership validation precedes token access, including for repeated preparations.
    const order = await this.orders.owned(session, orderId);
    if (order.state !== 'Created') throw new OrderError('PAY.ORDER_ALREADY_PAID', 409);
    const product = this.catalog.get(order.productId);
    if (!product) throw new OrderError('PAY.PRODUCT_NOT_FOUND', 404);
    const accessToken = await this.accessToken(order);
    // Recheck after an asynchronous credential lookup; a revoked game session must not start payment.
    await this.orders.owned(session, orderId);
    const result = await this.flow.create({ ...product, binding: this.binding, isSandbox: this.isSandbox,
      orderId, openId: order.subject, accessToken, orderUrl: this.orderUrlPrefix + encodeURIComponent(orderId) });
    return Object.freeze({ orderId, tradeOrderId: result.tradeOrderId });
  }
  async query(session, orderId) {
    const order = await this.orders.owned(session, orderId);
    const association = await this.flow.payments.getReadyForOrder({ binding: this.binding, isSandbox: this.isSandbox, orderId });
    if (!association || association.subject !== order.subject || association.openId !== order.subject)
      throw new OrderError('PAY.PREPAYMENT_NOT_FOUND', 404);
    const accessToken = await this.accessToken(order);
    await this.orders.owned(session, orderId);
    const result = await this.flow.client.query({ accessToken, tradeOrderId: association.tradeOrderId });
    if (!result || result.tradeOrderId !== association.tradeOrderId || !['PENDING', 'SUCCESS'].includes(result.status))
      throw new OrderError('PAY.QUERY_RESPONSE_INVALID', 502);
    // No state transition: only a verified webhook can establish payment.
    const current = await this.orders.owned(session, orderId);
    return Object.freeze({ orderId, platformStatus: result.status, state: current.state });
  }
  async accessToken(order) {
    let credential;
    try { credential = await this.credentials.get(this.binding, order.subject); }
    catch (error) {
      if(error instanceof OrderError && ['PAY.CREDENTIAL_REFRESH_PENDING','PAY.CREDENTIAL_REFRESH_UNCERTAIN'].includes(error.code))throw error;
      throw new OrderError('PAY.CREDENTIAL_UNAVAILABLE', 503);
    }
    const current = this.now();
    if (!Number.isSafeInteger(current) || !credential || credential.binding !== bindingKey(this.binding) ||
        credential.subject !== order.subject || !Number.isSafeInteger(credential.expiresAt) || credential.expiresAt <= current ||
        typeof credential.accessToken !== 'string' || !credential.accessToken || credential.accessToken.length > 8192 || /\s/.test(credential.accessToken))
      throw new OrderError('PAY.CREDENTIAL_UNAVAILABLE', 503);
    return credential.accessToken;
  }
}
