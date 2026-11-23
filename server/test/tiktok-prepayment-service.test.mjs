import test from 'node:test';
import assert from 'node:assert/strict';
import { AuthService, MemoryAuthStore, bindingKey } from '../src/auth.mjs';
import { OrderService } from '../src/orders.mjs';
import { SqliteOrderStore } from '../src/sqlite-orders.mjs';
import { TikTokPrepayments } from '../src/tiktok-prepayments.mjs';
import { TikTokPrepaymentFlow } from '../src/tiktok-prepayment-flow.mjs';
import { TikTokPrepaymentService } from '../src/tiktok-prepayment-service.mjs';
const binding = { gameId: 'game', appId: 'app', environment: 'sandbox', platform: 'TikTok', secret: 'test' };
async function fixture(t, tokenLookup) {
  const auth = new AuthService({ bindings: [binding], store: new MemoryAuthStore(), verifiers: { TikTok: async (_, code) => ({ subject: code }) } });
  const alice = (await auth.exchange({ ...binding, code: 'alice' })).session;
  const bob = (await auth.exchange({ ...binding, code: 'bob' })).session;
  const store = new SqliteOrderStore(':memory:'); t.after(() => store.close());
  const orders = new OrderService({ binding, auth, store, catalog: [{ productId: 'coins', amountMinor: 99, currency: 'USD' }], verifyPayment: async () => ({ paid: false }) });
  const order = await orders.create(alice, { productId: 'coins', requestId: 'one' });
  const requests = []; let lookups = 0;
  const flow = new TikTokPrepaymentFlow(new TikTokPrepayments(store), { create: async input => { requests.push(input); return { tradeOrderId: 'trade' }; } });
  const catalog = [{ productId: 'coins', productName: 'Coins', tokenAmount: 100, quantity: 1, quantityUnit: 'item', imageUrl: 'https://example.com/coins.png' }];
  const credentials = { get: async (scope, subject) => {
    lookups++; return tokenLookup ? tokenLookup(scope, subject, auth, alice) : { binding: bindingKey(scope), subject, accessToken: 'server-token', expiresAt: Date.now() + 60000 };
  } };
  const service = new TikTokPrepaymentService({ orders, flow, catalog, credentials, isSandbox: true });
  return { service, auth, alice, bob, order, requests, catalog, lookups: () => lookups };
}
test('authorized preparation uses frozen server catalog and returns no credential or identity', async t => {
  const f = await fixture(t); f.catalog[0].tokenAmount = 1;
  const result = await f.service.prepare(f.alice, f.order.orderId);
  assert.deepEqual(result, { orderId: f.order.orderId, tradeOrderId: 'trade' });
  assert.equal(f.requests[0].tokenAmount, 100); assert.equal(f.requests[0].accessToken, 'server-token');
  assert.equal(f.requests[0].productId, 'coins'); assert.equal(f.requests[0].orderUrl, '/orders/' + f.order.orderId);
  await f.service.prepare(f.alice, f.order.orderId); assert.equal(f.requests.length, 1);
});
test('foreign, missing and revoked game sessions cannot read credentials or start prepayment', async t => {
  const f = await fixture(t);
  await assert.rejects(f.service.prepare(f.bob, f.order.orderId), { code: 'PAY.ORDER_NOT_FOUND' });
  await assert.rejects(f.service.prepare('invalid', f.order.orderId), { code: 'AUTH.SESSION_INVALID' });
  await f.auth.revoke(f.alice);
  await assert.rejects(f.service.prepare(f.alice, f.order.orderId), { code: 'AUTH.SESSION_INVALID' });
  assert.equal(f.lookups(), 0); assert.equal(f.requests.length, 0);
});
test('wrong scope, user, expired token and provider exception fail without upstream creation', async t => {
  for (const patch of [{ binding: 'other' }, { subject: 'other' }, { expiresAt: 0 }, { accessToken: 'bad\n' }]) {
    const f = await fixture(t, (scope, subject) => ({ binding: bindingKey(scope), subject, expiresAt: Date.now() + 60000, accessToken: 'token', ...patch }));
    await assert.rejects(f.service.prepare(f.alice, f.order.orderId), { code: 'PAY.CREDENTIAL_UNAVAILABLE' });
    assert.equal(f.requests.length, 0);
  }
  const f = await fixture(t, () => { throw new Error('secret-token'); });
  await assert.rejects(f.service.prepare(f.alice, f.order.orderId), { message: 'PAY.CREDENTIAL_UNAVAILABLE' });
});
test('session revocation during credential lookup prevents platform call', async t => {
  const f = await fixture(t, async (scope, subject, auth, session) => {
    await auth.revoke(session); return { binding: bindingKey(scope), subject, expiresAt: Date.now() + 60000, accessToken: 'token' };
  });
  await assert.rejects(f.service.prepare(f.alice, f.order.orderId), { code: 'AUTH.SESSION_INVALID' });
  assert.equal(f.requests.length, 0);
});
