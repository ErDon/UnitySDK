import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { createHmac } from 'node:crypto';
import { SqliteOrderStore } from '../src/sqlite-orders.mjs';
import { TikTokPrepayments } from '../src/tiktok-prepayments.mjs';
import { createTikTokPaymentWebhookHandler } from '../src/tiktok-webhook-http.mjs';
import { bindingKey } from '../src/auth.mjs';
import { MemoryRateLimiter } from '../src/rate-limit.mjs';
const binding = { gameId: 'game', appId: 'app', environment: 'test', platform: 'TikTok' };
const clientSecret = 'test-only-secret', timestamp = 1800000000;
const raw = Buffer.from(JSON.stringify({ client_key: 'app', event: 'minis.trade_order.redeem.success',
  create_time: timestamp, user_openid: '', content: JSON.stringify({ order_id: 'one', trade_order_id: 'trade', is_sandbox: true }) }));
const sign = body => `t=${timestamp},s=${createHmac('sha256', clientSecret).update(`${timestamp}.`).update(body).digest('hex')}`;
async function fixture(t, options = {}) {
  const store = new SqliteOrderStore(':memory:'); t.after(() => store.close());
  await store.create('request', { orderId: 'one', binding: bindingKey(binding), subject: 'user', productId: 'coins', amountMinor: 99, currency: 'USD', createdAt: 1 });
  const payments = new TikTokPrepayments(store);
  await payments.register({ binding, isSandbox: true, orderId: 'one', tradeOrderId: 'trade', openId: 'user-openid', tokenAmount: 100 });
  const logs = [];
  const server = createServer(createTikTokPaymentWebhookHandler(payments, { binding, isSandbox: true, clientSecret,
    nowSeconds: () => timestamp, log: line => logs.push(line), ...options }));
  await new Promise((resolve, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', resolve); });
  t.after(() => new Promise(resolve => { server.close(resolve); server.closeAllConnections(); }));
  const send = async (body = raw, headers = {}, path = '/v1/webhooks/tiktok/payment') => {
    const response = await fetch(`http://127.0.0.1:${server.address().port}${path}`, { method: 'POST',
      headers: { 'Content-Type': 'application/json', 'TikTok-Signature': sign(body), ...headers }, body });
    return { status: response.status, headers: response.headers, data: await response.json() };
  };
  return { store, send, logs };
}
test('signed HTTP notification commits Paid before acknowledgment and duplicates are safe', async t => {
  const f = await fixture(t);
  const first = await f.send(); assert.equal(first.status, 200); assert.equal(first.data.received, true);
  assert.equal((await f.store.get('one')).state, 'Paid'); assert.equal((await f.send()).status, 200);
  assert.equal((await f.store.pendingDelivery()).length, 1);
  assert.equal(first.headers.get('cache-control'), 'no-store');
  assert.match(first.data.callId, /^[a-f0-9-]{36}$/);
  assert.equal(JSON.stringify(first.data).includes('trade'), false);
  for (const value of [clientSecret, 'user-openid', sign(raw), raw.toString()]) assert.equal(f.logs.join('').includes(value), false);
});
test('HTTP rejects tampering, malformed payloads, wrong routes and unsupported encoding', async t => {
  const f = await fixture(t);
  assert.equal((await f.send(Buffer.concat([raw, Buffer.from(' ')]), { 'TikTok-Signature': sign(raw) })).status, 401);
  assert.equal((await f.send(Buffer.from('{'))).status, 400);
  assert.equal((await f.send(raw, {}, '/v1/webhooks/tiktok/payment?token=secret')).status, 404);
  assert.equal((await f.send(raw, { 'Content-Type': 'text/plain' })).status, 415);
  assert.equal((await f.send(raw, { 'Content-Encoding': 'gzip' })).status, 415);
  assert.equal((await f.store.get('one')).state, 'Created');
});
test('HTTP bounds body and rate limits while logging failures do not fail valid notifications', async t => {
  const small = await fixture(t, { bodyLimit: 20 }); assert.equal((await small.send()).status, 413);
  const limited = await fixture(t, { limiter: new MemoryRateLimiter({ limit: 1 }), log: () => { throw new Error('private'); } });
  assert.equal((await limited.send()).status, 200);
  const rejected = await limited.send(); assert.equal(rejected.status, 429); assert.ok(Number(rejected.headers.get('retry-after')) > 0);
});
