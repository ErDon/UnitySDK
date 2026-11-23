import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { SqliteOrderStore } from '../src/sqlite-orders.mjs';
import { TikTokPrepayments } from '../src/tiktok-prepayments.mjs';
import { bindingKey } from '../src/auth.mjs';
const binding = { gameId: 'game', appId: 'app', environment: 'test', platform: 'TikTok' };
const association = { binding, isSandbox: true, orderId: 'one', tradeOrderId: 'trade', openId: 'openid', tokenAmount: 100 };
const order = (id, scope = binding) => ({ orderId: id, binding: bindingKey(scope), subject: 'user', productId: 'coins', amountMinor: 99, currency: 'USD', createdAt: 100 });
async function fixture(t, options) {
  const dir = await mkdtemp(join(tmpdir(), 'erdon-tiktok-prepayment-'));
  const stores = [];
  t.after(async () => { for (const store of stores) store.close(); await rm(dir, { recursive: true, force: true }); });
  const open = () => { const store = new SqliteOrderStore(join(dir, 'orders.sqlite'), options); stores.push(store); return store; };
  const store = open(); await store.create('one', order('one'));
  return { store, open, payments: new TikTokPrepayments(store) };
}
test('trusted association survives reopen with immutable values and idempotent registration', async t => {
  const f = await fixture(t);
  const result = await f.payments.register(association);
  assert.equal(result.tokenAmount, 100); assert.equal(result.subject, 'user'); assert.equal(Object.isFrozen(result), true);
  f.store.close(); const payments = new TikTokPrepayments(f.open());
  assert.deepEqual(await payments.get(association), result);
  assert.deepEqual(await payments.register(association), result);
});
test('transaction cannot be rebound to another order, identity, environment or price', async t => {
  const f = await fixture(t); await f.store.create('two', order('two')); await f.payments.register(association);
  for (const patch of [{ orderId: 'two' }, { openId: 'other' }, { isSandbox: false }, { tokenAmount: 200 }])
    await assert.rejects(f.payments.register({ ...association, ...patch }), { code: 'PAY.PREPAYMENT_CONFLICT' });
  assert.equal((await f.payments.get(association)).orderId, 'one');
});
test('lookups enforce game, application and environment scope', async t => {
  const f = await fixture(t); await f.payments.register(association);
  for (const patch of [{ gameId: 'other' }, { appId: 'other' }, { environment: 'production' }]) {
    const other = { ...association, binding: { ...binding, ...patch } };
    assert.equal(await f.payments.get(other), null);
    await assert.rejects(f.payments.register(other), { code: 'PAY.ORDER_NOT_FOUND' });
  }
  assert.equal(await f.payments.get({ ...association, isSandbox: false }), null);
});
test('capacity and paid-order guards preserve existing records', async t => {
  const f = await fixture(t, { capacity: 1 }); await f.payments.register(association);
  await assert.rejects(f.payments.register({ ...association, tradeOrderId: 'new' }), { code: 'SERVER.CAPACITY' });
  await f.store.markPaid('one', 'transaction');
  assert.equal((await f.payments.register(association)).tradeOrderId, 'trade');
  await assert.rejects(f.payments.register({ ...association, tradeOrderId: 'new' }), { code: 'PAY.ORDER_ALREADY_PAID' });
  f.store.close(); await assert.rejects(f.payments.get(association), { code: 'SERVER.STORAGE_UNAVAILABLE' });
});
