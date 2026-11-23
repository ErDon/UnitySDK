import test from 'node:test';
import assert from 'node:assert/strict';
import { SqliteOrderStore } from '../src/sqlite-orders.mjs';
import { TikTokPrepayments } from '../src/tiktok-prepayments.mjs';
import { TikTokPrepaymentFlow } from '../src/tiktok-prepayment-flow.mjs';
import { bindingKey } from '../src/auth.mjs';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
const binding = { gameId: 'game', appId: 'app', environment: 'test', platform: 'TikTok' };
const input = { binding, isSandbox: true, openId: 'user-openid', accessToken: 'secret', orderId: 'one',
  tokenAmount: 100, productName: 'Coins', orderUrl: '/orders/one', quantity: 1, quantityUnit: 'item', imageUrl: 'https://example.com/a.png' };
async function fixture(t) {
  const store = new SqliteOrderStore(':memory:'); t.after(() => store.close());
  await store.create('req', { orderId: 'one', binding: bindingKey(binding), subject: 'user', productId: 'coins', amountMinor: 99, currency: 'USD', createdAt: 1 });
  return { store, payments: new TikTokPrepayments(store) };
}
test('successful prepayment is persisted and subsequent identical calls reuse it', async t => {
  const f = await fixture(t); let calls = 0;
  const client = { create: async args => { calls++; assert.equal(args.productId, 'coins'); return { tradeOrderId: 'trade' }; } };
  const flow = new TikTokPrepaymentFlow(f.payments, client);
  assert.deepEqual(await flow.create(input), { tradeOrderId: 'trade' });
  assert.deepEqual(await new TikTokPrepaymentFlow(f.payments, client).create({ ...input, accessToken: 'refreshed' }), { tradeOrderId: 'trade' });
  assert.equal(calls, 1); assert.equal((await f.payments.get({ ...input, tradeOrderId: 'trade' })).orderId, 'one');
  await assert.rejects(flow.create({ ...input, tokenAmount: 200 }), { code: 'PAY.PREPAYMENT_CONFLICT' });
});
test('in-flight and failed requests retain durable Pending rather than retrying', async t => {
  const f = await fixture(t); let reject, calls = 0;
  const client = { create: () => { calls++; return new Promise((_, fail) => { reject = fail; }); } };
  const flow = new TikTokPrepaymentFlow(f.payments, client), first = flow.create(input);
  await assert.rejects(flow.create(input), { code: 'PAY.PREPAYMENT_UNCERTAIN' });
  reject(new Error('transport unavailable')); await assert.rejects(first);
  await assert.rejects(new TikTokPrepaymentFlow(f.payments, client).create(input), { code: 'PAY.PREPAYMENT_UNCERTAIN' });
  assert.equal(calls, 1);
  assert.equal(f.store.db.prepare('SELECT state FROM erdon_tiktok_attempts').get().state, 'Pending');
});
test('foreign or paid order cannot trigger platform creation', async t => {
  const f = await fixture(t); let calls = 0;
  const flow = new TikTokPrepaymentFlow(f.payments, { create: async () => { calls++; } });
  await assert.rejects(flow.create({ ...input, binding: { ...binding, gameId: 'other' } }), { code: 'PAY.ORDER_NOT_FOUND' });
  await f.store.markPaid('one', 'txn');
  await assert.rejects(flow.create(input), { code: 'PAY.ORDER_ALREADY_PAID' }); assert.equal(calls, 0);
});
test('Pending survives closing and reopening the database and blocks another upstream request', async t => {
  const dir = await mkdtemp(join(tmpdir(), 'erdon-tiktok-flow-')), stores = [];
  t.after(async () => { for (const store of stores) store.close(); await rm(dir, { recursive: true, force: true }); });
  const open = () => { const store = new SqliteOrderStore(join(dir, 'orders.sqlite')); stores.push(store); return store; };
  let store = open(); await store.create('req', { orderId: 'one', binding: bindingKey(binding), subject: 'user', productId: 'coins', amountMinor: 99, currency: 'USD', createdAt: 1 });
  let calls = 0; const client = { create: async () => { calls++; throw new Error('lost response'); } };
  await assert.rejects(new TikTokPrepaymentFlow(new TikTokPrepayments(store), client).create(input)); store.close();
  store = open();
  await assert.rejects(new TikTokPrepaymentFlow(new TikTokPrepayments(store), client).create(input), { code: 'PAY.PREPAYMENT_UNCERTAIN' });
  assert.equal(calls, 1);
});
test('failure updating Ready rolls back the platform association in the same transaction', async t => {
  const f = await fixture(t); let calls = 0;
  const flow = new TikTokPrepaymentFlow(f.payments, { create: async () => { calls++; return { tradeOrderId: 'trade' }; } });
  f.store.db.exec(`CREATE TRIGGER fail_ready BEFORE UPDATE ON erdon_tiktok_attempts
    WHEN NEW.state='Ready' BEGIN SELECT RAISE(ABORT,'injected storage failure'); END;`);
  await assert.rejects(flow.create(input), { code: 'SERVER.STORAGE_UNAVAILABLE' });
  assert.equal(await f.payments.get({ ...input, tradeOrderId: 'trade' }), null);
  assert.equal(f.store.db.prepare('SELECT state FROM erdon_tiktok_attempts').get().state, 'Pending');
  await assert.rejects(flow.create(input), { code: 'PAY.PREPAYMENT_UNCERTAIN' });
  assert.equal(calls, 1);
});
test('invalid preflight parameters never reserve an attempt and corrected input can proceed', async t => {
  const f = await fixture(t); let calls = 0;
  const flow = new TikTokPrepaymentFlow(f.payments, { create: async () => { calls++; return { tradeOrderId: 'trade' }; } });
  for (const patch of [{ imageUrl: 'http://example.com/a' }, { accessToken: 'token\nvalue' },
    { openId: 'user\nvalue' }, { productName: 'x'.repeat(257) }, { orderUrl: 'x'.repeat(2049) }]) {
    await assert.rejects(flow.create({ ...input, ...patch }), TypeError);
    assert.equal(f.store.db.prepare('SELECT COUNT(*) AS count FROM erdon_tiktok_attempts').get().count, 0);
  }
  assert.equal(calls, 0);
  assert.deepEqual(await flow.create(input), { tradeOrderId: 'trade' }); assert.equal(calls, 1);
});
