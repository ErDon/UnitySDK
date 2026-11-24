import test from 'node:test';
import assert from 'node:assert/strict';
import { randomBytes } from 'node:crypto';
import { TikTokCredentialStore } from '../src/tiktok-credential-store.mjs';
import { TikTokCredentials } from '../src/tiktok-credentials.mjs';

const binding = { gameId: 'game', appId: 'app', environment: 'test', platform: 'TikTok' };
const initial = { accessToken: 'old-access', expiresAt: 2000, refreshToken: 'old-refresh', refreshExpiresAt: 100000 };
const renewed = { subject: 'alice', accessToken: 'new-access', expiresAt: 50000, refreshToken: 'new-refresh', refreshExpiresAt: 200000 };
function fixture(t) {
  let now = 1000;
  const store = new TikTokCredentialStore(':memory:', { keys: new Map([['key', randomBytes(32)]]), currentKeyId: 'key', now: () => now });
  t.after(() => store.close());
  return { store, now: () => now, advance: value => { now = value; } };
}

test('concurrent callers share refresh and persist rotated credentials after access expiry', async t => {
  const f = fixture(t); await f.store.put(binding, 'alice', initial); f.advance(3000);
  let resolve, calls = 0;
  const wait = new Promise(done => { resolve = done; });
  const manager = new TikTokCredentials({ binding, store: f.store, now: f.now, refreshBeforeMs: 1000,
    client: { async refresh(actual, subject, token) { calls++; assert.equal(token, 'old-refresh'); await wait; return renewed; } } });
  const first = manager.get(binding, 'alice'), second = manager.get(binding, 'alice');
  assert.equal(first, second); resolve();
  assert.equal((await first).accessToken, 'new-access'); assert.equal(calls, 1);
  assert.equal((await f.store.getRefresh(binding, 'alice')).refreshToken, 'new-refresh');
  assert.equal((await manager.get(binding, 'alice')).accessToken, 'new-access'); assert.equal(calls, 1);
});

for (const action of ['remove', 'login']) test(`late refresh cannot overwrite concurrent ${action}`, async t => {
  const f = fixture(t); await f.store.put(binding, 'alice', initial);
  let finish, started;
  const pending = new Promise(resolve => { finish = resolve; });
  const entered = new Promise(resolve => { started = resolve; });
  const manager = new TikTokCredentials({ binding, store: f.store, now: f.now,
    client: { async refresh() { started(); await pending; return renewed; } } });
  const request = manager.get(binding, 'alice'); await entered;
  if (action === 'remove') await f.store.remove(binding, 'alice');
  else await f.store.put(binding, 'alice', { ...renewed, accessToken: 'login-access', refreshToken: 'login-refresh' });
  finish();
  assert.equal((await request)?.accessToken ?? null, action === 'remove' ? null : 'login-access');
  assert.equal((await f.store.getRefresh(binding, 'alice'))?.refreshToken ?? null, action === 'remove' ? null : 'login-refresh');
});

test('refresh errors are sanitized and later calls can retry; scope mismatch cannot call upstream', async t => {
  const f = fixture(t); await f.store.put(binding, 'alice', initial); let calls = 0;
  const manager = new TikTokCredentials({ binding, store: f.store, now: f.now,
    client: { async refresh() { calls++; if (calls === 1) throw new Error('secret upstream token'); return renewed; } } });
  await assert.rejects(manager.get({ ...binding, appId: 'other' }, 'alice'), { code: 'PAY.CREDENTIAL_UNAVAILABLE' });
  assert.equal(calls, 0);
  await assert.rejects(manager.get(binding, 'alice'), { code: 'PAY.CREDENTIAL_UNAVAILABLE', message: 'PAY.CREDENTIAL_UNAVAILABLE' });
  assert.equal((await manager.get(binding, 'alice')).accessToken, 'new-access'); assert.equal(calls, 2);
});

test('vault refresh revision cannot resurrect removed credentials and purpose AAD prevents swapping', async t => {
  const f = fixture(t); await f.store.put(binding, 'alice', initial);
  const revision = (await f.store.getRefresh(binding, 'alice')).revision;
  await f.store.remove(binding, 'alice');
  assert.equal(await f.store.put(binding, 'alice', renewed, revision), false);
  assert.equal(await f.store.get(binding, 'alice'), null);
  await f.store.put(binding, 'alice', initial);
  f.store.db.exec('DELETE FROM erdon_tiktok_refresh_credentials; INSERT INTO erdon_tiktok_refresh_credentials SELECT * FROM erdon_tiktok_credentials;');
  await assert.rejects(f.store.getRefresh(binding, 'alice'), { code: 'PAY.CREDENTIAL_UNAVAILABLE' });
});
