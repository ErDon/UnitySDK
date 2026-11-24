import { AuthService } from './auth.mjs';
import { SqliteAuthStore } from './sqlite-store.mjs';
import { SqliteOrderStore } from './sqlite-orders.mjs';
import { createDouyinVerifier } from './douyin.mjs';
import { OrderService } from './orders.mjs';
import { DouyinPayments } from './douyin-payments.mjs';
import { createAuthHandler } from './http.mjs';
import { createOrderHandler } from './orders-http.mjs';
import { createDouyinPaymentWebhookHandler } from './douyin-webhook-http.mjs';
import { DeliveryWorker } from './delivery-worker.mjs';
import { createDouyinQueryClient } from './douyin-query-client.mjs';

// One trusted application/route per instance. Drain HTTP before awaiting close().
export function createDouyinApplication({ binding, catalog, callbackToken, authPath, orderPath,
  route = 'Android', zoneId = '1', fetchImpl = fetch, now = Date.now, log = console.info,
  deliver = null, deliveryIntervalMs = 3000, deliveryBatchSize = 100 } = {}) {
  if (binding?.platform !== 'Douyin' || typeof now !== 'function' || typeof log !== 'function' ||
      !Array.isArray(catalog) || !catalog.length || (deliver !== null && typeof deliver !== 'function'))
    throw new TypeError('Invalid Douyin application configuration');
  const owned = []; let worker = null, closed = false, closing = null;
  const closeStores = () => { for (const store of owned.reverse()) { try { store.close(); } catch { } } };
  const close = () => {
    if (closed) return closing;
    closed = true;
    if (worker) { closing = worker.stop().finally(closeStores); return closing; }
    closeStores();
  };
  try {
    const authStore = new SqliteAuthStore(authPath, { now }); owned.push(authStore);
    const orderStore = new SqliteOrderStore(orderPath); owned.push(orderStore);
    const auth = new AuthService({ bindings: [binding], store: authStore, now,
      verifiers: { Douyin: createDouyinVerifier({ fetchImpl }) } });
    // 当前由签名通知确认 Paid；客户端 confirm 不能凭收银台成功回调把订单标记为已付款。
    const orders = new OrderService({ binding, auth, store: orderStore, catalog, now,
      verifyPayment: async () => ({ paid: false }) });
    const payments = new DouyinPayments({ orders, catalog, route, zoneId,
      queryClient: createDouyinQueryClient({ binding, fetchImpl, now }) });
    const sessionHandler = createAuthHandler(auth, { log });
    const orderHandler = createOrderHandler(orders, { prepayments: payments, log });
    const webhookHandler = createDouyinPaymentWebhookHandler(payments, { callbackToken, log });
    const handler = (request, response) => {
      if (closed) {
        response.writeHead(503, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store', 'Connection': 'close' });
        response.end(JSON.stringify({ errorCode: 'SERVER.CLOSED' })); return;
      }
      if (request.url === '/v1/session' || request.url === '/v1/session/revoke-all') return sessionHandler(request, response);
      if (request.url?.split('?')[0] === '/v1/webhooks/douyin/payment') return webhookHandler(request, response);
      return orderHandler(request, response);
    };
    // 未注入游戏发货处理时保留 Paid；不得仅为返回成功而伪造 Fulfilled。
    // deliver 必须按订单幂等入库，工作器重试不能导致重复发放权益。
    if (deliver !== null) {
      worker = new DeliveryWorker({ orders, deliver, intervalMs: deliveryIntervalMs, batchSize: deliveryBatchSize, log }); worker.start();
    }
    return Object.freeze({ handler, close, orders, pendingDelivery: () => orderStore.pendingDelivery() });
  } catch (error) { close(); throw error; }
}
