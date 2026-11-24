import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { AuthService, MemoryAuthStore, bindingKey, digest } from '../src/auth.mjs';
import { SqliteAuthStore } from '../src/sqlite-store.mjs';
import { createAuthHandler } from '../src/http.mjs';

const binding = { platform: 'TikTok', gameId: 'game', appId: 'app', environment: 'test', secret: 'private-secret' };
const second = { ...binding, gameId: 'second' }, production = { ...binding, environment: 'production' };
const auth = (store, verify = async (b, code) => ({ subject: code.split(':')[0] })) => new AuthService({
  bindings: [binding, second, production], store, verifiers: { TikTok: verify }
});
const login = async (service, code, scope = binding) => (await service.exchange({ ...scope, code })).session;

for (const kind of ['memory', 'sqlite']) {
  test(`${kind}: revoke all is scoped to subject, game and environment; fresh login remains possible`, async t => {
    const store = kind === 'memory' ? new MemoryAuthStore() : new SqliteAuthStore(':memory:');
    t.after(() => store.close?.()); const service = auth(store);
    const a = await login(service, 'alice:a'), b = await login(service, 'alice:b'), other = await login(service, 'bob:a');
    const game = await login(service, 'alice:game', second), prod = await login(service, 'alice:prod', production);
    await assert.rejects(service.revokeAll(a, second), { code: 'AUTH.SESSION_INVALID' });
    assert.deepEqual(await service.revokeAll(a, binding), { revoked: true, scope: 'game-sessions' });
    for (const session of [a, b]) await assert.rejects(service.authenticate(session, binding), { code: 'AUTH.SESSION_INVALID' });
    for (const [session, scope] of [[other, binding], [game, second], [prod, production]]) assert.ok(await service.authenticate(session, scope));
    assert.ok(await service.authenticate(await login(service, 'alice:new'), binding));
  });
  test(`${kind}: revocation rejects late login commit but does not reject another subject's pending login`, async t => {
    const store = kind === 'memory' ? new MemoryAuthStore() : new SqliteAuthStore(':memory:');
    t.after(() => store.close?.()); const normal = auth(store), current = await login(normal, 'alice:first');
    let entered; const started = new Promise(resolve => { entered = resolve; }); let release;
    const delayed = auth(store, () => new Promise(resolve => { release = resolve; entered(); }));
    const pending = login(delayed, 'alice:pending'); await started;
    await normal.revokeAll(current, binding); release({ subject: 'alice' });
    await assert.rejects(pending, { code: 'AUTH.LOGIN_REVOKED' });
    const rev = await store.sessionRevision();
    await store.revokeSubject(bindingKey(binding), 'alice');
    await store.putSession('bob-hash', { binding: bindingKey(binding), subject: 'bob', expiresAt: Date.now() + 60000 }, rev);
    assert.equal((await store.getSession('bob-hash')).subject, 'bob');
  });
  test(`${kind}: revocation capacity failure preserves sessions atomically`, async t => {
    const store = kind === 'memory' ? new MemoryAuthStore({ capacity: 1 }) : new SqliteAuthStore(':memory:', { capacity: 1 });
    t.after(() => store.close?.());
    await store.revokeSubject('scope', 'first');
    await store.putSession('hash', { binding: 'scope', subject: 'second', expiresAt: Date.now() + 60000 }, await store.sessionRevision());
    await assert.rejects(store.revokeSubject('scope', 'second'), { code: 'SERVER.CAPACITY' });
    assert.ok(await store.getSession('hash'));
  });
}

test('SQLite revocation persists across restart and blocks stale commits from another connection', async t => {
  const dir = await mkdtemp(join(tmpdir(), 'erdon-revoke-')), path = join(dir, 'auth.sqlite');
  let a = new SqliteAuthStore(path), b = new SqliteAuthStore(path);
  t.after(async () => { a.close(); b.close(); await rm(dir, { recursive: true, force: true }); });
  const service = auth(a), session = await login(service, 'alice:a'), revision = await b.sessionRevision();
  await service.revokeAll(session, binding);
  await assert.rejects(b.putSession('late', { binding: bindingKey(binding), subject: 'alice', expiresAt: Date.now() + 60000 }, revision), { code: 'AUTH.LOGIN_REVOKED' });
  a.close(); b.close(); a = new SqliteAuthStore(path); b = new SqliteAuthStore(path);
  await assert.rejects(auth(a).authenticate(session, binding), { code: 'AUTH.SESSION_INVALID' });
  assert.ok(await auth(b).authenticate(await login(auth(a), 'alice:fresh'), binding));
});

test('v1 SQLite migration preserves existing sessions and claims', async t => {
  const dir = await mkdtemp(join(tmpdir(), 'erdon-auth-v1-')), path = join(dir, 'auth.sqlite');
  const db = new DatabaseSync(path);
  db.exec(`CREATE TABLE erdon_auth_schema(version INTEGER NOT NULL); INSERT INTO erdon_auth_schema VALUES(1);
    CREATE TABLE erdon_auth_claims(hash TEXT PRIMARY KEY,expires_at INTEGER NOT NULL);
    CREATE TABLE erdon_auth_sessions(hash TEXT PRIMARY KEY,binding TEXT NOT NULL,subject TEXT NOT NULL,expires_at INTEGER NOT NULL);`);
  const session = 'a'.repeat(43), expiry = Date.now() + 60000;
  db.prepare('INSERT INTO erdon_auth_sessions VALUES(?,?,?,?)').run(digest(session), bindingKey(binding), 'alice', expiry);
  db.prepare('INSERT INTO erdon_auth_claims VALUES(?,?)').run('old-claim', expiry); db.close();
  const store = new SqliteAuthStore(path);
  t.after(async () => { store.close(); await rm(dir, { recursive: true, force: true }); });
  assert.equal(store.db.prepare('SELECT version FROM erdon_auth_schema').get().version, 2);
  assert.equal((await auth(store).authenticate(session, binding)).subject, 'alice');
  assert.equal(await store.claim('old-claim', expiry), false);
  await auth(store).revokeAll(session, binding); assert.equal(await store.getSession(digest(session)), null);
});

test('HTTP revoke-all authenticates identity, rejects alternate routes and keeps diagnostics redacted', async t => {
  const service = auth(new MemoryAuthStore()), session = await login(service, 'alice:http'), other = await login(service, 'bob:http');
  const logs = [], server = createServer(createAuthHandler(service, { log: line => logs.push(line) }));
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  t.after(() => new Promise(resolve => server.close(resolve)));
  const base = `http://127.0.0.1:${server.address().port}`;
  const send = (path, body, bearer = session) => fetch(base + path, { method: 'POST', headers: {
    'Content-Type': 'application/json', Authorization: 'Bearer ' + bearer }, body: JSON.stringify(body) });
  for (const [path, body, bearer, status] of [
    ['/v1/session/revoke-all?token=secret', binding, session, 404], ['/v1/session/revoke-all', binding, '', 401],
    ['/v1/session/revoke-all', second, session, 401], ['/v1/session/revoke-all', null, session, 400],
    ['/v1/session/revoke-all', { ...binding, subject: 'bob', token: other }, session, 200]
  ]) { const res = await send(path, body, bearer); assert.equal(res.status, status); const data = await res.json(); assert.ok(data.callId); }
  await assert.rejects(service.authenticate(session, binding), { code: 'AUTH.SESSION_INVALID' });
  assert.equal((await service.authenticate(other, binding)).subject, 'bob');
  assert.ok(logs.some(line => JSON.parse(line).interface === 'Session.RevokeAll'));
  assert.ok(!logs.join('').includes(session) && !logs.join('').includes(other) && !logs.join('').includes('private-secret'));
});
