import test from 'node:test';
import assert from 'node:assert/strict';
import { AuthService, MemoryAuthStore } from '../src/auth.mjs';
import { OrderService, MemoryOrderStore } from '../src/orders.mjs';
import { SqliteAuthStore } from '../src/sqlite-store.mjs';
import { SqliteOrderStore } from '../src/sqlite-orders.mjs';

const base = { gameId: 'game-a', appId: 'app-a', environment: 'test', platform: 'WeChat', secret: 'test-only' };
// Every binding is authorized: rejection must be isolation, not an unknown-app check.
const bindings = [base, { ...base, gameId: 'game-b' }, { ...base, environment: 'production' },
  { ...base, appId: 'app-b' }, { ...base, platform: 'Douyin' }, { ...base, platform: 'TikTok' }];

for (const kind of ['memory', 'sqlite']) {
  test(`${kind}: authorized games retain session, order and credential isolation in shared stores`, async t => {
    const store = kind === 'memory' ? new MemoryAuthStore() : new SqliteAuthStore(':memory:');
    const orders = kind === 'memory' ? new MemoryOrderStore() : new SqliteOrderStore(':memory:');
    t.after(() => { store.close?.(); orders.close?.(); });
    let verifications = 0;
    const verify = async () => { verifications++; return { subject: 'same-platform-subject' }; };
    const auth = new AuthService({ bindings, store, verifiers: { WeChat: verify, Douyin: verify, TikTok: verify } });
    const sessions = [];
    for (let i = 0; i < bindings.length; i++)
      sessions.push((await auth.exchange({ ...bindings[i], code: `fresh-${i}` })).session);
    assert.equal(new Set(sessions).size, bindings.length);

    const services = bindings.map((binding, i) => new OrderService({ binding, auth, store: orders,
      catalog: [{ productId: 'coins', amountMinor: 100 + i, currency: 'USD' }],
      verifyPayment: async () => { throw new Error('No payment should be invoked'); } }));
    const created = [];
    for (let i = 0; i < bindings.length; i++) {
      created.push(await services[i].create(sessions[i], { productId: 'coins', requestId: 'same-request-id' }));
      assert.equal(created[i].amountMinor, 100 + i);
      assert.equal((await services[i].create(sessions[i], { productId: 'coins', requestId: 'same-request-id' })).orderId,
        created[i].orderId);
    }
    assert.equal(new Set(created.map(order => order.orderId)).size, bindings.length);
    for (let i = 0; i < bindings.length; i++) {
      assert.equal((await auth.authenticate(sessions[i], bindings[i])).subject, 'same-platform-subject');
      assert.equal((await services[i].get(sessions[i], created[i].orderId)).orderId, created[i].orderId);
      for (let j = 0; j < bindings.length; j++) {
        if (i === j) continue;
        await assert.rejects(auth.authenticate(sessions[i], bindings[j]), { code: 'AUTH.SESSION_INVALID' });
        await assert.rejects(services[j].create(sessions[i], { productId: 'coins', requestId: 'foreign' }),
          { code: 'AUTH.SESSION_INVALID' });
        await assert.rejects(services[j].get(sessions[j], created[i].orderId), { code: 'PAY.ORDER_NOT_FOUND' });
        await assert.rejects(services[j].confirm(sessions[j], created[i].orderId, 'test-evidence'),
          { code: 'PAY.ORDER_NOT_FOUND' });
      }
    }

    // One application code cannot be consumed again through another authorized game/environment.
    await auth.exchange({ ...base, code: 'shared-code' });
    const beforeReplay = verifications;
    for (const binding of bindings.slice(1, 3))
      await assert.rejects(auth.exchange({ ...binding, code: 'shared-code' }), { code: 'AUTH.CODE_REUSED' });
    assert.equal(verifications, beforeReplay);
    // Other app/platform namespaces may independently issue the same opaque code text.
    for (const binding of bindings.slice(3)) {
      const session = (await auth.exchange({ ...binding, code: 'shared-code' })).session;
      assert.equal((await auth.authenticate(session, binding)).subject, 'same-platform-subject');
    }
    await auth.revoke(sessions[0]);
    await assert.rejects(auth.authenticate(sessions[0], bindings[0]), { code: 'AUTH.SESSION_INVALID' });
    for (let i = 1; i < bindings.length; i++)
      assert.equal((await auth.authenticate(sessions[i], bindings[i])).subject, 'same-platform-subject');
  });
}
