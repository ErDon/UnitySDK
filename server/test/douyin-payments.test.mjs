import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { createServer } from 'node:http';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { AuthService, MemoryAuthStore } from '../src/auth.mjs';
import { OrderService } from '../src/orders.mjs';
import { SqliteOrderStore } from '../src/sqlite-orders.mjs';
import { DouyinPayments } from '../src/douyin-payments.mjs';
import { verifyDouyinWebhook, readDouyinPaymentWebhook } from '../src/douyin-webhook.mjs';
import { createDouyinPaymentWebhookHandler } from '../src/douyin-webhook-http.mjs';
import { createOrderHandler } from '../src/orders-http.mjs';
import { MemoryRateLimiter } from '../src/rate-limit.mjs';
import { createDouyinApplication } from '../src/douyin-application.mjs';

export const token = 'private-callback-token';
export function signed(msg, changes = {}) {
  const value = { timestamp: '1800000000', nonce: 'unique-nonce', msg: typeof msg === 'string' ? msg : JSON.stringify(msg), ...changes };
  value.signature = createHash('sha1').update([token, value.timestamp, value.nonce, value.msg].sort().join('')).digest('hex');
  return value;
}
export async function fixture(t, { route = 'Android', path = ':memory:' } = {}) {
  const binding = { gameId: 'game', appId: 'app', environment: 'test', platform: 'Douyin', secret: 'secret' };
  const catalog = [{ productId: 'sword', productName: '宝剑', amountMinor: 600, currency: 'CNY' }];
  const auth = new AuthService({ bindings: [binding], verifiers: { Douyin: async (b, code) => ({ subject: code }) }, store: new MemoryAuthStore() });
  const session = (await auth.exchange({ ...binding, code: 'alice' })).session;
  const other = (await auth.exchange({ ...binding, code: 'bob' })).session;
  const store = new SqliteOrderStore(path); t.after(() => store.close());
  const orders = new OrderService({ binding, auth, catalog, store, verifyPayment: async () => ({ paid: false }) });
  const payments = new DouyinPayments({ orders, catalog, route });
  const order = await orders.create(session, { productId: 'sword', requestId: 'first' });
  return { binding, auth, session, other, store, orders, payments, order, catalog };
}
export const message = (ready, overrides = {}) => ({ appid: 'app', cp_orderno: ready.orderId, cp_extra: ready.extraInfo,
  order_no_channel: 'transaction', amount_cent: ready.amountMinor, amount_coin: 0, currency: 'CNY', ...overrides });

test('signature authenticates exact msg and bounded fields before decoding payment', () => {
  const envelope = signed(' { "x": 1 } ');
  assert.equal(verifyDouyinWebhook(envelope, token).valid, true);
  for (const altered of [{ ...envelope, msg: '{"x":1}' }, { ...envelope, nonce: 'other' },
    { ...envelope, signature: 'a'.repeat(40) }, { ...envelope, timestamp: 1800000000 }, { ...envelope, msg: 'x'.repeat(32769) }])
    assert.equal(verifyDouyinWebhook(altered, token).valid, false);
  assert.equal(readDouyinPaymentWebhook(signed('not json'), { callbackToken: token, appId: 'app' }).code, 'WEBHOOK.INVALID_PAYLOAD');
  assert.equal(verifyDouyinWebhook(envelope, 'wrong-token').valid, false);
});

test('prepared direct purchase retains price/nonce, denies other users and commits signed payment once', async t => {
  const f = await fixture(t), ready = await f.payments.prepare(f.session, f.order.orderId);
  assert.equal(ready.amountMinor, 600); assert.equal(ready.buyQuantity, 0); assert.equal(ready.directPurchase, true);
  assert.deepEqual(await f.payments.prepare(f.session, f.order.orderId), ready);
  await assert.rejects(f.payments.prepare(f.other, f.order.orderId), { code: 'PAY.ORDER_NOT_FOUND' });
  await assert.rejects(f.orders.confirm(f.session, f.order.orderId, 'client-success'), { code: 'PAY.NOT_PAID' });
  const envelope = signed(message(ready));
  assert.equal((await f.payments.acceptWebhook(envelope, token)).state, 'Paid');
  let granted = 0; await f.orders.fulfill(ready.orderId, async () => { granted++; return true; });
  assert.deepEqual(await f.payments.acceptWebhook(envelope, token), { accepted: true, duplicate: true, state: 'Fulfilled' });
  await f.orders.fulfill(ready.orderId, async () => { granted++; return true; }); assert.equal(granted, 1);
  await assert.rejects(f.payments.prepare(f.session, ready.orderId), { code: 'PAY.ORDER_ALREADY_PAID' });
});

test('validly signed wrong association, price, tender, application and reused transaction cannot fund order', async t => {
  const f = await fixture(t), ready = await f.payments.prepare(f.session, f.order.orderId);
  for (const overrides of [{ cp_extra: 'forged' }, { amount_cent: 1 }, { currency: 'DIAMOND' }]) {
    await assert.rejects(f.payments.acceptWebhook(signed(message(ready, overrides)), token), { code: 'PAY.VERIFICATION_MISMATCH' });
    assert.equal((await f.store.get(ready.orderId)).state, 'Created');
  }
  for (const overrides of [{ appid: 'other' }, { amount_cent: 0 }, { amount_coin: -1 }, { amount_cent: 6.5 }])
    assert.equal((await f.payments.acceptWebhook(signed(message(ready, overrides)), token)).accepted, false);
  await f.payments.acceptWebhook(signed(message(ready)), token);
  const next = await f.orders.create(f.session, { productId: 'sword', requestId: 'second' });
  const other = await f.payments.prepare(f.session, next.orderId);
  await assert.rejects(f.payments.acceptWebhook(signed(message(other)), token), { code: 'PAY.TRANSACTION_REUSED' });
  await assert.rejects(f.payments.acceptWebhook(signed(message(ready, { order_no_channel: 'different' })), token), { code: 'PAY.TRANSACTION_CONFLICT' });
});

test('iOS records diamond tender but verifies the server CNY-cent price, and revoked sessions cannot prepare', async t => {
  const f = await fixture(t, { route: 'IosCustomerService' }), ready = await f.payments.prepare(f.session, f.order.orderId);
  assert.equal(ready.route, 'IosCustomerService');
  await assert.rejects(f.payments.acceptWebhook(signed(message(ready)), token), { code: 'PAY.VERIFICATION_MISMATCH' });
  assert.equal((await f.payments.acceptWebhook(signed(message(ready, { currency: 'DIAMOND' })), token)).state, 'Paid');
  await f.auth.revoke(f.session);
  await assert.rejects(f.payments.prepare(f.session, ready.orderId), { code: 'AUTH.SESSION_INVALID' });
});

test('preparation and Paid survive restart; catalog changes do not rewrite already prepared parameters', async t => {
  const dir = await mkdtemp(join(tmpdir(), 'erdon-douyin-'));
  const f = await fixture(t, { path: join(dir, 'orders.sqlite') });
  const ready = await f.payments.prepare(f.session, f.order.orderId); f.store.close();
  const reopened = new SqliteOrderStore(join(dir, 'orders.sqlite'));
  t.after(async () => { reopened.close(); await rm(dir, { recursive: true, force: true }); });
  const orders = new OrderService({ binding: f.binding, auth: f.auth, store: reopened,
    catalog: [{ ...f.catalog[0], amountMinor: 900 }], verifyPayment: async () => ({ paid: false }) });
  const payments = new DouyinPayments({ orders, catalog: [{ ...f.catalog[0], productName: '新名字' }], route: 'IosCustomerService' });
  assert.deepEqual(await payments.prepare(f.session, ready.orderId), ready);
  await payments.acceptWebhook(signed(message(ready)), token);
  assert.equal((await reopened.pendingDelivery()).length, 1);
  const otherScope = new OrderService({ binding: { ...f.binding, environment: 'other' }, auth: f.auth, store: reopened,
    catalog: f.catalog, verifyPayment: async () => ({ paid: false }) });
  const otherPayments = new DouyinPayments({ orders: otherScope, catalog: f.catalog });
  await assert.rejects(otherPayments.acceptWebhook(signed(message(ready)), token), { code: 'PAY.PREPAYMENT_NOT_FOUND' });
});

async function httpFixture(t, options = {}) {
  const f = await fixture(t), logs = [];
  const webhook = createDouyinPaymentWebhookHandler(f.payments, { callbackToken: token, log: value => logs.push(value), ...options });
  const orderHandler = createOrderHandler(f.orders, { prepayments: f.payments, log: value => logs.push(value) });
  const server = createServer((req, res) => req.url.startsWith('/v1/orders') ? orderHandler(req, res) : webhook(req, res));
  await new Promise((resolve, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', resolve); });
  t.after(() => new Promise(resolve => { server.close(resolve); server.closeAllConnections(); }));
  const base = `http://127.0.0.1:${server.address().port}`;
  const post = (body, headers = {}, path = '/v1/webhooks/douyin/payment') => fetch(base + path,
    { method: 'POST', headers: { 'Content-Type': 'application/json', ...headers }, body: typeof body === 'string' || Buffer.isBuffer(body) ? body : JSON.stringify(body) });
  return { ...f, base, post, logs };
}

test('HTTP handshake and payment commit use signed data, ignore client prices and keep diagnostics redacted', async t => {
  const f = await httpFixture(t);
  const handshake = signed('', { echostr: '<echo>' });
  let res = await fetch(f.base + '/v1/webhooks/douyin/payment?' + new URLSearchParams(handshake));
  assert.equal(res.status, 200); assert.equal(await res.text(), '<echo>'); assert.match(res.headers.get('content-type'), /text\/plain/);
  res = await fetch(f.base + '/v1/webhooks/douyin/payment?' + new URLSearchParams(handshake) + '&nonce=other');
  assert.equal(res.status, 400); await res.text();
  res = await f.post({ amountMinor: 1, extraInfo: 'forged', route: 'IosCustomerService' }, { Authorization: 'Bearer ' + f.session }, `/v1/orders/${f.order.orderId}/prepare`);
  assert.equal(res.status, 200); const ready = (await res.json()).prepayment;
  assert.equal(ready.amountMinor, 600); assert.equal(ready.route, 'Android'); assert.notEqual(ready.extraInfo, 'forged');
  for (let i = 0; i < 2; i++) {
    res = await f.post(signed(message(ready))); assert.equal(res.status, 200);
    const data = await res.json(); assert.equal(data.received, true); assert.ok(data.callId);
  }
  assert.equal((await f.store.get(ready.orderId)).state, 'Paid');
  assert.equal((await f.store.pendingDelivery()).length, 1);
  for (const secret of [token, ready.extraInfo, f.session, signed(message(ready)).signature]) assert.equal(f.logs.join('').includes(secret), false);
});

test('HTTP rejects malformed, unsigned and oversized requests; rate limiting and bad log sinks are contained', async t => {
  const f = await httpFixture(t), ready = await f.payments.prepare(f.session, f.order.orderId);
  const cases = [
    ['{', {}, 400], [Buffer.from([0xff]), {}, 400], [signed(message(ready)), { 'Content-Type': 'text/plain' }, 415],
    [signed(message(ready)), { 'Content-Encoding': 'gzip' }, 415], [{ ...signed(message(ready)), signature: '0'.repeat(40) }, {}, 401],
    ['x'.repeat(65537), {}, 413]
  ];
  for (const [body, headers, status] of cases) { const res = await f.post(body, headers); assert.equal(res.status, status); await res.text(); }
  assert.equal((await f.store.get(ready.orderId)).state, 'Created');
  const limited = await httpFixture(t, { limiter: new MemoryRateLimiter({ limit: 1 }), log: () => { throw new Error('secret'); } });
  const launch = await limited.payments.prepare(limited.session, limited.order.orderId);
  let res = await limited.post(signed(message(launch))); assert.equal(res.status, 200); await res.text();
  res = await limited.post(signed(message(launch))); assert.equal(res.status, 429);
  assert.ok(Number(res.headers.get('retry-after')) > 0); await res.text();
});

test('composed Douyin service exchanges login, prepares, receives payment and delivers through worker', async t => {
  const binding = { gameId: 'game', appId: 'app', environment: 'test', platform: 'Douyin', secret: 'secret' };
  let delivered, granted = 0;
  const received = new Promise(resolve => { delivered = resolve; });
  const app = createDouyinApplication({ binding, callbackToken: token, authPath: ':memory:', orderPath: ':memory:',
    catalog: [{ productId: 'sword', productName: '宝剑', amountMinor: 600, currency: 'CNY' }], log: () => {},
    fetchImpl: async () => Response.json({ error: 0, openid: 'alice', session_key: 'platform-key' }),
    deliveryIntervalMs: 100, deliver: async value => { assert.equal(value.subject, 'alice'); granted++; delivered(); return true; } });
  const server = createServer(app.handler);
  t.after(async () => { await new Promise(resolve => { server.close(resolve); server.closeAllConnections(); }); await app.close(); });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const base = `http://127.0.0.1:${server.address().port}`;
  const post = async (path, body, session) => {
    const res = await fetch(base + path, { method: 'POST', headers: { 'Content-Type': 'application/json',
      ...(session ? { Authorization: 'Bearer ' + session } : {}) }, body: JSON.stringify(body) });
    assert.equal(res.status, 200); return res.json();
  };
  const login = await post('/v1/session', { ...binding, secret: undefined, code: 'first' });
  assert.ok(!JSON.stringify(login).includes('platform-key'));
  const order = (await post('/v1/orders', { productId: 'sword', requestId: 'one' }, login.session)).order;
  const ready = (await post(`/v1/orders/${order.orderId}/prepare`, {}, login.session)).prepayment;
  await post('/v1/webhooks/douyin/payment', signed(message(ready)));
  let timer;
  try { await Promise.race([received, new Promise((resolve, reject) => { timer = setTimeout(() => reject(new Error('Delivery timed out')), 2000); })]); }
  finally { clearTimeout(timer); }
  await app.close();
  assert.equal(granted, 1);
  const res = await fetch(base + '/v1/session'); assert.equal(res.status, 503); await res.text();
});
