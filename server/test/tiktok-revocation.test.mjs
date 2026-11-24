import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { AuthService, bindingKey } from '../src/auth.mjs';
import { SqliteAuthStore } from '../src/sqlite-store.mjs';
import { TikTokCredentialStore } from '../src/tiktok-credential-store.mjs';
import { TikTokCredentials } from '../src/tiktok-credentials.mjs';
import { TikTokRevocation } from '../src/tiktok-revocation.mjs';
import { createTikTokApplication } from '../src/tiktok-application.mjs';
import { createTikTokVerifier } from '../src/tiktok.mjs';

const binding = { gameId: 'game', appId: 'app', environment: 'test', platform: 'TikTok', secret: 'private-secret' };
const keys = new Map([['key', Buffer.alloc(32, 4)]]);
const value = (token = 'private-access') => ({ accessToken: token, expiresAt: Date.now() + 60000,
  refreshToken: 'private-refresh', refreshExpiresAt: Date.now() + 3600000 });
async function setup(t, { client = { revoke: async () => {}, refresh: async () => { throw new Error(); } } } = {}) {
  const dir = await mkdtemp(join(tmpdir(), 'erdon-revoke-platform-'));
  const credentialPath = join(dir, 'credentials.db'), authPath = join(dir, 'auth.db');
  const stores = []; t.after(async () => { for (const store of stores) store.close(); await rm(dir, { recursive: true, force: true }); });
  const open = () => {
    const credentials = new TikTokCredentialStore(credentialPath, { keys, currentKeyId: 'key' });
    const authStore = new SqliteAuthStore(authPath); stores.push(credentials, authStore);
    let revoke;
    const auth = new AuthService({ bindings: [binding], store: authStore,
      assertAuthorized: (b, subject) => revoke.assertAuthorized(b, subject),
      verifiers: { TikTok: async (b, code) => ({ subject: code.split(':')[0] }) } });
    const managedCredentials = new TikTokCredentials({ binding, store: credentials, client, refreshBeforeMs: 0 });
    revoke = new TikTokRevocation({ binding, auth, credentials, managedCredentials, client });
    return { credentials, authStore, auth, revoke, managedCredentials };
  };
  const f = open();
  await f.credentials.put(binding, 'alice', value());
  const session = (await f.auth.exchange({ ...binding, code: 'alice:first' })).session;
  return { ...f, session, open, credentialPath };
}

test('platform revoke removes both tokens and every local session, without affecting other subjects', async t => {
  let calls = 0;
  const f = await setup(t, { client: { refresh: async () => {}, revoke: async (b, token) => {
    calls++; assert.equal(b.appId, binding.appId); assert.equal(token, 'private-access');
  } } });
  const second = (await f.auth.exchange({ ...binding, code: 'alice:second' })).session;
  const bob = (await f.auth.exchange({ ...binding, code: 'bob:first' })).session;
  await f.credentials.put(binding, 'bob', value('bob-token'));
  assert.deepEqual(await f.revoke.revoke(f.session), { revoked: true, scope: 'tiktok-authorization' });
  assert.equal(calls, 1); assert.equal(f.credentials.revocation(binding, 'alice').state, 'Complete');
  for (const s of [f.session, second]) await assert.rejects(f.auth.authenticate(s, binding), { code: 'AUTH.SESSION_INVALID' });
  assert.equal(await f.credentials.get(binding, 'alice'), null); assert.equal(await f.credentials.getRefresh(binding, 'alice'), null);
  assert.ok(await f.auth.authenticate(bob, binding)); assert.equal((await f.credentials.get(binding, 'bob')).accessToken, 'bob-token');
  await f.revoke.resume('alice'); assert.equal(calls, 1);
});

test('credential lookup failure reports its cause and does not start a revocation', async t => {
  const f = await setup(t);
  f.managedCredentials.get = async () => { throw new Error('private refresh error'); };
  await assert.rejects(f.revoke.revoke(f.session), { code: 'AUTH.CREDENTIAL_UNAVAILABLE', message: 'AUTH.CREDENTIAL_UNAVAILABLE' });
  assert.equal(f.credentials.revocation(binding, 'alice'), null);
  assert.ok(await f.auth.authenticate(f.session, binding));
});

test('in-flight revoke blocks token use and login; concurrent requests coalesce', async t => {
  let release, entered; const started = new Promise(resolve => { entered = resolve; }); let calls = 0;
  const f = await setup(t, { client: { refresh: async () => {}, revoke: () => { calls++; entered(); return new Promise(resolve => { release = resolve; }); } } });
  const pending = f.revoke.revoke(f.session); await started;
  const duplicate = f.revoke.revoke(f.session);
  await assert.rejects(f.auth.authenticate(f.session, binding), { code: 'AUTH.REVOCATION_PENDING' });
  await assert.rejects(f.auth.exchange({ ...binding, code: 'alice:blocked' }), { code: 'AUTH.REVOCATION_PENDING' });
  assert.equal(await f.credentials.get(binding, 'alice'), null);
  await assert.rejects(f.credentials.put(binding, 'alice', value('new')));
  await assert.rejects(f.credentials.remove(binding, 'alice'));
  release(); await Promise.all([pending, duplicate]); assert.equal(calls, 1);
});

test('uncertain remote result persists through restart; only explicit recovery retries it', async t => {
  let calls = 0, fail = true;
  const f = await setup(t, { client: { refresh: async () => {}, revoke: async () => { calls++; if (fail) throw new Error('private-access'); } } });
  await assert.rejects(f.revoke.revoke(f.session), { code: 'AUTH.REVOCATION_UNCERTAIN' });
  assert.equal(f.credentials.revocation(binding, 'alice').state, 'Uncertain');
  f.credentials.close(); f.authStore.close(); const next = f.open();
  await assert.rejects(next.auth.authenticate(f.session, binding), { code: 'AUTH.REVOCATION_PENDING' });
  await assert.rejects(next.revoke.revoke(f.session), { code: 'AUTH.REVOCATION_UNCERTAIN' });
  await assert.rejects(next.revoke.resume('alice'), { code: 'AUTH.REVOCATION_UNCERTAIN' }); assert.equal(calls, 1);
  fail = false; await next.revoke.resume('alice', { previousWorkerStopped: true }); assert.equal(calls, 2);
  assert.equal(next.credentials.revocation(binding, 'alice').state, 'Complete');
});

test('durable confirmation resumes local cleanup without another platform revoke', async t => {
  let calls = 0;
  const f = await setup(t, { client: { refresh: async () => {}, revoke: async () => { calls++; } } });
  const original = f.authStore.revokeSubject.bind(f.authStore);
  f.authStore.revokeSubject = async () => { throw new Error('database failure'); };
  await assert.rejects(f.revoke.revoke(f.session), { code: 'AUTH.REVOCATION_STORAGE_FAILED' });
  assert.equal(f.credentials.revocation(binding, 'alice').state, 'Finalizing');
  f.authStore.revokeSubject = original;
  await f.revoke.resume('alice', { previousWorkerStopped: true }); assert.equal(calls, 1);
  assert.equal(await f.credentials.getRefresh(binding, 'alice'), null);
});

test('cleanup resumes after sessions were deleted but credential commit failed', async t => {
  let calls = 0;
  const f = await setup(t, { client: { refresh: async () => {}, revoke: async () => { calls++; } } });
  const complete = f.credentials.completeRevocation.bind(f.credentials);
  f.credentials.completeRevocation = () => { throw new Error('commit failed'); };
  await assert.rejects(f.revoke.revoke(f.session), { code: 'AUTH.REVOCATION_STORAGE_FAILED' });
  await assert.rejects(f.revoke.revoke(f.session), { code: 'AUTH.SESSION_INVALID' });
  assert.equal(f.credentials.revocation(binding, 'alice').state, 'Finalizing');
  f.credentials.completeRevocation = complete;
  f.credentials.close(); f.authStore.close(); const next = f.open();
  await next.revoke.resume('alice', { previousWorkerStopped: true });
  assert.equal(calls, 1); assert.equal(await next.credentials.get(binding, 'alice'), null);
});

test('two connections claim only one remote operation and stale jobs cannot delete a new authorization', async t => {
  let release, entered; const started = new Promise(resolve => { entered = resolve; });
  const f = await setup(t, { client: { refresh: async () => {}, revoke: () => { entered(); return new Promise(resolve => { release = resolve; }); } } });
  const oldRevision = f.credentials.authorizationRevision();
  const oldRefresh = await f.credentials.getRefresh(binding, 'alice');
  const pending = f.revoke.revoke(f.session); await started;
  const second = f.open(), job = second.credentials.revocation(binding, 'alice');
  await assert.rejects(second.revoke.revoke(f.session), { code: 'AUTH.REVOCATION_UNCERTAIN' });
  release(); await pending;
  await assert.rejects(second.credentials.put(binding, 'alice', value('late-login'), null, oldRevision));
  assert.equal(await second.credentials.put(binding, 'alice', value('late-refresh'), oldRefresh.revision), false);
  await second.credentials.put(binding, 'alice', value('fresh-login'), null, second.credentials.authorizationRevision());
  second.credentials.completeRevocation(binding, 'alice', job.jobId);
  assert.equal((await second.credentials.get(binding, 'alice')).accessToken, 'fresh-login');
});

test('late OAuth login cannot restore tokens after revoke finishes; fresh login can authorize again', async t => {
  const f = await setup(t); let release, entered; const started = new Promise(resolve => { entered = resolve; });
  const verifier = createTikTokVerifier({ credentialStore: f.credentials, fetchImpl: () => { entered(); return new Promise(resolve => { release = resolve; }); } });
  const response = () => Response.json({ open_id: 'alice', token_type: 'Bearer', access_token: 'new-token', expires_in: 3600 });
  const pending = verifier(binding, 'pending'); await started;
  await f.revoke.revoke(f.session); release(response());
  await assert.rejects(pending, { code: 'AUTH.LOGIN_REVOKED' });
  assert.equal(await f.credentials.get(binding, 'alice'), null);
  await createTikTokVerifier({ credentialStore: f.credentials, fetchImpl: async () => response() })(binding, 'fresh');
  assert.equal((await f.credentials.get(binding, 'alice')).accessToken, 'new-token');
});

test('composed HTTP authorization revoke authenticates bearer and never accepts a client-selected token', async t => {
  let revokedToken; const logs = [];
  const app = createTikTokApplication({ binding, isSandbox: true, authPath: ':memory:', orderPath: ':memory:', credentialPath: ':memory:',
    keys, currentKeyId: 'key', log: line => logs.push(line),
    catalog: [{ productId: 'coins', amountMinor: 99, currency: 'USD', tokenAmount: 100, productName: 'Coins', quantity: 1, quantityUnit: 'item', imageUrl: 'https://example.com/a.png' }],
    fetchImpl: async (url, options) => {
      if (url.endsWith('/revoke/')) { revokedToken = new URLSearchParams(options.body).get('token'); return Response.json({}); }
      return Response.json({ open_id: 'alice', token_type: 'Bearer', access_token: 'stored-token', expires_in: 3600 });
    } });
  const server = createServer(app.handler); await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  t.after(async () => { await new Promise(resolve => server.close(resolve)); await app.close(); });
  const base = `http://127.0.0.1:${server.address().port}`;
  const post = async (path, data, session = '') => {
    const response = await fetch(base + path, { method: 'POST', headers: { 'Content-Type': 'application/json', Authorization: 'Bearer ' + session }, body: JSON.stringify(data) });
    return { status: response.status, data: await response.json() };
  };
  const session = (await post('/v1/session', { ...binding, code: 'one' })).data.session;
  assert.equal((await post('/v1/authorization/revoke', {})).status, 401);
  assert.equal((await post('/v1/authorization/revoke?token=bad', {}, session)).status, 404);
  const result = await post('/v1/authorization/revoke', { token: 'attacker-token', subject: 'bob' }, session);
  assert.equal(result.status, 200); assert.equal(result.data.scope, 'tiktok-authorization'); assert.ok(result.data.callId);
  assert.equal(revokedToken, 'stored-token');
  assert.equal((await post('/v1/orders', { productId: 'coins', requestId: 'one' }, session)).status, 401);
  assert.ok(!logs.join('').includes('stored-token') && !logs.join('').includes(session));
});

test('credential v1 migration retains encrypted tokens and revocation retains expired recovery material', async t => {
  const f = await setup(t);
  f.credentials.close();
  const db = new DatabaseSync(f.credentialPath);
  db.exec('DROP TABLE erdon_tiktok_authorization_revision; DROP TABLE erdon_tiktok_revocations; UPDATE erdon_tiktok_credential_schema SET version=1;'); db.close();
  const next = f.open();
  assert.equal(next.credentials.db.prepare('SELECT version FROM erdon_tiktok_credential_schema').get().version, 2);
  assert.equal((await next.credentials.get(binding, 'alice')).accessToken, 'private-access');
  const job = next.credentials.beginRevocation(binding, 'alice');
  next.credentials.now = () => Date.now() + 7200000;
  await next.credentials.put(binding, 'bob', { accessToken: 'bob', expiresAt: Date.now() + 10800000 });
  assert.equal(await next.credentials.get(binding, 'alice'), null);
  assert.ok(next.credentials.transitionRevocation(binding, 'alice', job.jobId, 'Pending', 'Sending'));
  assert.equal(next.credentials.read(binding, 'alice', false, job.jobId).accessToken, 'private-access');
  const content = next.credentials.db.prepare('SELECT ciphertext FROM erdon_tiktok_credentials WHERE subject=?').get('alice');
  assert.ok(!Buffer.from(content.ciphertext).includes(Buffer.from('private-access')));
});
