import test from 'node:test';
import assert from 'node:assert/strict';
import { randomBytes } from 'node:crypto';
import { mkdtemp, rm, readFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { TikTokCredentialStore } from '../src/tiktok-credential-store.mjs';
const binding = { gameId: 'game', appId: 'app', environment: 'test', platform: 'TikTok' };
const secret = 'private-access-token-for-storage-test';
async function fixture(t) {
  const dir = await mkdtemp(join(tmpdir(), 'erdon-vault-')), stores = [], keys = new Map([['key1', randomBytes(32)]]);
  const path = join(dir, 'credentials.sqlite');
  t.after(async () => { for (const store of stores) store.close(); await rm(dir, { recursive: true, force: true }); });
  const open = options => { const store = new TikTokCredentialStore(path, { keys, currentKeyId: 'key1', now: () => 1000, ...options }); stores.push(store); return store; };
  return { path, keys, open };
}
test('encrypted credential survives reopening without token plaintext in database', async t => {
  const f = await fixture(t); let store = f.open(); await store.put(binding, 'alice', { accessToken: secret, expiresAt: 2000 });
  assert.equal((await store.get(binding, 'alice')).accessToken, secret); store.close();
  assert.equal((await readFile(f.path)).includes(Buffer.from(secret)), false);
  store = f.open(); assert.equal((await store.get(binding, 'alice')).accessToken, secret);
  assert.equal(await store.get({ ...binding, gameId: 'other' }, 'alice'), null); assert.equal(await store.get(binding, 'bob'), null);
  await store.remove(binding, 'alice'); assert.equal(await store.get(binding, 'alice'), null);
});
test('AAD rejects changed expiry or subject and wrong keys fail without leaking data', async t => {
  const f = await fixture(t); const store = f.open(); await store.put(binding, 'alice', { accessToken: secret, expiresAt: 2000 });
  store.db.prepare('UPDATE erdon_tiktok_credentials SET expires_at=3000').run();
  await assert.rejects(store.get(binding, 'alice'), { code: 'PAY.CREDENTIAL_UNAVAILABLE', message: 'PAY.CREDENTIAL_UNAVAILABLE' });
  await store.put(binding, 'alice', { accessToken: secret, expiresAt: 2000 });
  store.db.prepare("UPDATE erdon_tiktok_credentials SET subject='bob'").run();
  await assert.rejects(store.get(binding, 'bob'), { code: 'PAY.CREDENTIAL_UNAVAILABLE' });
  await store.put(binding, 'alice', { accessToken: secret, expiresAt: 2000 }); store.close();
  const wrong = f.open({ keys: new Map([['key1', randomBytes(32)]]) });
  await assert.rejects(wrong.get(binding, 'alice'), { code: 'PAY.CREDENTIAL_UNAVAILABLE' });
});
test('rotation reads old key and rewrites with current key; caller buffers are not modified', async t => {
  const f = await fixture(t), original = Buffer.from(f.keys.get('key1')); let store = f.open();
  await store.put(binding, 'alice', { accessToken: secret, expiresAt: 2000 }); store.close();
  assert.deepEqual(f.keys.get('key1'), original);
  const keys = new Map([...f.keys, ['key2', randomBytes(32)]]);
  store = f.open({ keys, currentKeyId: 'key2' }); assert.equal((await store.get(binding, 'alice')).accessToken, secret);
  await store.put(binding, 'alice', { accessToken: 'new-token', expiresAt: 3000 });
  assert.equal(store.db.prepare('SELECT key_id FROM erdon_tiktok_credentials').get().key_id, 'key2');
  store.close(); store = f.open({ keys: new Map([['key2', keys.get('key2')]]), currentKeyId: 'key2' });
  assert.equal((await store.get(binding, 'alice')).accessToken, 'new-token');
});
test('expiration, capacity and closed store preserve safe behavior', async t => {
  const f = await fixture(t); let now = 1000; const store = f.open({ capacity: 1, now: () => now });
  await store.put(binding, 'alice', { accessToken: secret, expiresAt: 2000 });
  await assert.rejects(store.put(binding, 'bob', { accessToken: secret, expiresAt: 2000 }), { code: 'PAY.CREDENTIAL_UNAVAILABLE' });
  now = 2000; assert.equal(await store.get(binding, 'alice'), null);
  await store.put(binding, 'bob', { accessToken: secret, expiresAt: 3000 });
  assert.equal((await store.get(binding, 'bob')).subject, 'bob'); store.close();
  await assert.rejects(store.get(binding, 'bob'), { code: 'PAY.CREDENTIAL_UNAVAILABLE' });
});
