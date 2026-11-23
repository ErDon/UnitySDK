import { AuthService } from './auth.mjs';
import { SqliteAuthStore } from './sqlite-store.mjs';
import { SqliteOrderStore } from './sqlite-orders.mjs';
import { TikTokCredentialStore } from './tiktok-credential-store.mjs';
import { createTikTokVerifier } from './tiktok.mjs';
import { OrderService } from './orders.mjs';
import { TikTokPrepayments } from './tiktok-prepayments.mjs';
import { TikTokPrepaymentFlow } from './tiktok-prepayment-flow.mjs';
import { TikTokPrepaymentService } from './tiktok-prepayment-service.mjs';
import { createTikTokTradeClient } from './tiktok-trade-client.mjs';
import { createAuthHandler } from './http.mjs';
import { createOrderHandler } from './orders-http.mjs';
import { createTikTokPaymentWebhookHandler } from './tiktok-webhook-http.mjs';

// One application binding per instance. Caller drains its HTTP server before close().
// This composition owns its databases, but never starts a listener or grants goods.
export function createTikTokApplication({ binding, catalog, isSandbox, authPath, orderPath, credentialPath,
  keys, currentKeyId, orderUrlPrefix = '/orders/', fetchImpl = fetch, now = Date.now, log = console.info } = {}) {
  if (binding?.platform !== 'TikTok' || typeof isSandbox !== 'boolean' ||
      typeof now !== 'function' || typeof log !== 'function' || !Array.isArray(catalog) || !catalog.length)
    throw new TypeError('Invalid TikTok application configuration');
  const scope = Object.freeze({ ...binding });
  const owned = [];
  let closed = false;
  const close = () => {
    if (closed) return;
    closed = true;
    for (const store of owned.reverse()) { try { store.close(); } catch { /* Close remaining resources. */ } }
  };
  try {
    const credentials = new TikTokCredentialStore(credentialPath, { keys, currentKeyId, now }); owned.push(credentials);
    const authStore = new SqliteAuthStore(authPath, { now }); owned.push(authStore);
    const orderStore = new SqliteOrderStore(orderPath); owned.push(orderStore);
    const auth = new AuthService({ bindings: [scope], store: authStore, now,
      verifiers: { TikTok: createTikTokVerifier({ fetchImpl, credentialStore: credentials, now }) } });
    const orders = new OrderService({ binding: scope, catalog, auth, store: orderStore, now,
      // Client-supplied evidence cannot mark a TikTok order paid. Signed notifications do that.
      verifyPayment: async () => ({ paid: false }) });
    const payments = new TikTokPrepayments(orderStore);
    const flow = new TikTokPrepaymentFlow(payments, createTikTokTradeClient({ fetchImpl }));
    const prepayments = new TikTokPrepaymentService({ orders, flow, catalog, credentials, isSandbox, now, orderUrlPrefix });
    const sessionHandler = createAuthHandler(auth, { log });
    const orderHandler = createOrderHandler(orders, { log, prepayments });
    const webhookHandler = createTikTokPaymentWebhookHandler(payments, { binding: scope, isSandbox,
      clientSecret: scope.secret, log, nowSeconds: () => Math.floor(now() / 1000) });
    const handler = (request, response) => {
      if (closed) {
        response.writeHead(503, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store', 'Connection': 'close' });
        response.end(JSON.stringify({ errorCode: 'SERVER.CLOSED' })); return;
      }
      if (request.url === '/v1/session') return sessionHandler(request, response);
      if (request.url === '/v1/webhooks/tiktok/payment') return webhookHandler(request, response);
      return orderHandler(request, response); // Existing strict router rejects all other paths.
    };
    return Object.freeze({ handler, close, orders,
      pendingDelivery: () => orderStore.pendingDelivery() });
  } catch (error) { close(); throw error; }
}
