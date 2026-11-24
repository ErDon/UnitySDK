import test from 'node:test';
import assert from 'node:assert/strict';
import { AuthService, MemoryAuthStore, bindingKey } from '../src/auth.mjs';
import { OrderService, OrderError } from '../src/orders.mjs';
import { SqliteOrderStore } from '../src/sqlite-orders.mjs';
import { TikTokPrepayments } from '../src/tiktok-prepayments.mjs';
import { TikTokPrepaymentFlow } from '../src/tiktok-prepayment-flow.mjs';
import { TikTokPrepaymentService } from '../src/tiktok-prepayment-service.mjs';
import { createServer } from 'node:http';
import { createOrderHandler } from '../src/orders-http.mjs';
const binding = { gameId: 'game', appId: 'app', environment: 'sandbox', platform: 'TikTok', secret: 'test' };
async function fixture(t, tokenLookup) {
  const auth = new AuthService({ bindings: [binding], store: new MemoryAuthStore(), verifiers: { TikTok: async (_, code) => ({ subject: code }) } });
  const alice = (await auth.exchange({ ...binding, code: 'alice' })).session;
  const bob = (await auth.exchange({ ...binding, code: 'bob' })).session;
  const store = new SqliteOrderStore(':memory:'); t.after(() => store.close());
  const orders = new OrderService({ binding, auth, store, catalog: [{ productId: 'coins', amountMinor: 99, currency: 'USD' }], verifyPayment: async () => ({ paid: false }) });
  const order = await orders.create(alice, { productId: 'coins', requestId: 'one' });
  const requests = [], queries = []; let lookups = 0;
  const flow = new TikTokPrepaymentFlow(new TikTokPrepayments(store), {
    create: async input => { requests.push(input); return { tradeOrderId: 'trade' }; },
    query: async input => { queries.push(input); return { tradeOrderId: 'trade', status: 'SUCCESS' }; }
  });
  const catalog = [{ productId: 'coins', productName: 'Coins', tokenAmount: 100, quantity: 1, quantityUnit: 'item', imageUrl: 'https://example.com/coins.png' }];
  const credentials = { get: async (scope, subject) => {
    lookups++; return tokenLookup ? tokenLookup(scope, subject, auth, alice) : { binding: bindingKey(scope), subject, accessToken: 'server-token', expiresAt: Date.now() + 60000 };
  } };
  const service = new TikTokPrepaymentService({ orders, flow, catalog, credentials, isSandbox: true });
  return { service, orders, auth, alice, bob, order, requests, queries, catalog, lookups: () => lookups };
}

test('query authenticates before credentials, requires saved association and never marks paid', async t => {
  const f = await fixture(t), id=f.order.orderId;
  await assert.rejects(f.service.query(f.bob,id),{code:'PAY.ORDER_NOT_FOUND'});
  await assert.rejects(f.service.query(f.alice,id),{code:'PAY.PREPAYMENT_NOT_FOUND'});
  assert.equal(f.lookups(),0);
  await f.service.prepare(f.alice,id);
  assert.deepEqual(await f.service.query(f.alice,id),{orderId:id,platformStatus:'SUCCESS',state:'Created'});
  assert.deepEqual(f.queries,[{accessToken:'server-token',tradeOrderId:'trade'}]);
  assert.equal((await f.orders.get(f.alice,id)).state,'Created');
  f.service.isSandbox=false;
  await assert.rejects(f.service.query(f.alice,id),{code:'PAY.PREPAYMENT_NOT_FOUND'});
  assert.equal(f.queries.length,1);
});

test('preparation preserves known refresh recovery codes without leaking arbitrary upstream errors',async t=>{
  for(const code of ['PAY.CREDENTIAL_REFRESH_PENDING','PAY.CREDENTIAL_REFRESH_UNCERTAIN']){
    const f=await fixture(t,()=>{throw new OrderError(code,503);});
    await assert.rejects(f.service.prepare(f.alice,f.order.orderId),{code,status:503});
    assert.equal(f.requests.length,0);
  }
});

test('query rechecks session after credential lookup and platform response', async t => {
  const f=await fixture(t), id=f.order.orderId;
  await f.service.prepare(f.alice,id);
  f.service.credentials.get=async()=>{
    await f.auth.revoke(f.alice);
    return {binding:bindingKey(binding),subject:'alice',accessToken:'token',expiresAt:Date.now()+60000};
  };
  await assert.rejects(f.service.query(f.alice,id),{code:'AUTH.SESSION_INVALID'});
  assert.equal(f.queries.length,0);
  const g=await fixture(t);await g.service.prepare(g.alice,g.order.orderId);
  g.service.flow.client.query=async()=>{await g.auth.revoke(g.alice);return {tradeOrderId:'trade',status:'SUCCESS'};};
  await assert.rejects(g.service.query(g.alice,g.order.orderId),{code:'AUTH.SESSION_INVALID'});
});
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
test('HTTP prepare authenticates ownership, ignores client prices and credentials, and redacts logs', async t => {
  const f = await fixture(t), logs = [];
  const server = createServer(createOrderHandler(f.orders, { prepayments: f.service, log: line => logs.push(line) }));
  await new Promise((resolve, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', resolve); });
  t.after(() => new Promise(resolve => { server.close(resolve); server.closeAllConnections(); }));
  const send = async session => {
    const response = await fetch(`http://127.0.0.1:${server.address().port}/v1/orders/${f.order.orderId}/prepare`, {
      method: 'POST', headers: { Authorization: 'Bearer ' + session, 'Content-Type': 'application/json' },
      body: JSON.stringify({ tokenAmount: 1, accessToken: 'attacker-token', openId: 'attacker', orderId: 'other' }) });
    return { status: response.status, body: await response.json() };
  };
  assert.equal((await send(f.bob)).status, 404); assert.equal(f.lookups(), 0);
  const success = await send(f.alice); assert.equal(success.status, 200);
  assert.deepEqual(success.body.prepayment, { orderId: f.order.orderId, tradeOrderId: 'trade' });
  assert.match(success.body.callId, /^[a-f0-9-]{36}$/);
  assert.equal(f.requests[0].tokenAmount, 100); assert.equal(f.requests[0].accessToken, 'server-token');
  await f.auth.revoke(f.alice); assert.equal((await send(f.alice)).status, 401);
  assert.equal(f.requests.length, 1);
  for (const secret of ['attacker-token', 'server-token', f.alice, f.bob]) assert.equal(logs.join('').includes(secret), false);
  assert.equal(JSON.stringify(success.body).includes('server-token'), false);
});
