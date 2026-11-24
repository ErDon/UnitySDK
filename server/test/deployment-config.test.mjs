import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile, mkdtemp, rm, writeFile, readdir } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import { tmpdir } from 'node:os';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { fileURLToPath } from 'node:url';
import { resolveDeploymentConfig, loadDeploymentConfig } from '../src/deployment-config.mjs';
import { startConfiguredServer } from '../src/configured-server.mjs';

const env = { WECHAT_APP_ID: 'wx-app', WECHAT_APP_SECRET: 'private-wechat-secret',
  DOUYIN_APP_ID: 'tt-app', DOUYIN_APP_SECRET: 'private-douyin-secret', DOUYIN_CALLBACK_TOKEN: 'private-callback-token',
  TIKTOK_APP_ID: 'tk-app', TIKTOK_APP_SECRET: 'private-tiktok-secret', TIKTOK_CREDENTIAL_KEY_HEX: 'ab'.repeat(32) };
const template = async name => JSON.parse(await readFile(new URL(`../config/${name}.example.json`, import.meta.url), 'utf8'));
async function directory(t) {
  const dir = await mkdtemp(join(tmpdir(), 'erdon-config-'));
  t.after(() => rm(dir, { recursive: true, force: true })); return dir;
}

test('all templates preview missing names without credentials, files or platform calls', async t => {
  const dir = await directory(t);
  for (const platform of ['wechat', 'douyin', 'tiktok']) {
    const raw = await template(platform);
    const missing = resolveDeploymentConfig(raw, { env: {}, baseDir: dir, requireSecrets: false });
    assert.equal(missing.preview.ready, false);
    assert.ok(missing.preview.missingVariables.includes(raw.secretEnv));
    assert.throws(() => resolveDeploymentConfig(raw, { env: {}, baseDir: dir }), { code: 'CONFIG.ENV_MISSING' });
    const ready = resolveDeploymentConfig(raw, { env, baseDir: dir });
    assert.equal(ready.preview.ready, true); assert.equal(ready.configuration.binding.secret, env[raw.secretEnv]);
    assert.equal(ready.configuration.authPath, resolve(dir, raw.storage.auth));
    for (const value of Object.values(env)) assert.equal(JSON.stringify(ready.preview).includes(value), false);
  }
  assert.deepEqual(await readdir(dir), []);
});

test('invalid or incompatible configuration fails with field-only diagnostics', async () => {
  const original = await template('douyin');
  const mutations = [
    raw => { raw.secret = 'do-not-echo-private-secret'; }, raw => { raw.schemaVersion = 9; },
    raw => { raw.payments.route = 'unknown'; }, raw => { raw.payments.catalog[0].amountMinor = -1; },
    raw => { raw.payments.catalog.push(raw.payments.catalog[0]); }, raw => { raw.storage.orders = raw.storage.auth; },
    raw => { raw.storage.auth = ':memory:'; }, raw => { raw.payments.enabled = 'true'; },
    raw => { raw.appIdEnv = 'private invalid name'; }, raw => { raw.port = 65536; },
    raw => { raw.payments.catalog[0].currency = 'USD'; }, raw => { raw.payments.isSandbox = true; }
  ];
  for (const change of mutations) {
    const raw = structuredClone(original); change(raw);
    assert.throws(() => resolveDeploymentConfig(raw, { env }), error => error.code.startsWith('CONFIG.') && !error.message.includes('private'));
  }
  const wx = await template('wechat'); wx.payments.enabled = true;
  assert.throws(() => resolveDeploymentConfig(wx, { env }), { code: 'CONFIG.PAYMENT_UNIMPLEMENTED' });
  const tk = await template('tiktok');
  assert.throws(() => resolveDeploymentConfig(tk, { env: { ...env, TIKTOK_CREDENTIAL_KEY_HEX: 'private-invalid-key' } }),
    { code: 'CONFIG.ENV_INVALID', message: 'CONFIG.ENV_INVALID: payments.keysEnv' });
  tk.payments.keysEnv.old = 'OLD_KEY';
  const rotated = resolveDeploymentConfig(tk, { env: { ...env, OLD_KEY: 'cd'.repeat(32) } });
  assert.equal(rotated.configuration.keys.size, 2);
});

test('loader bounds bytes, rejects invalid JSON/UTF8 and resolves paths relative to file', async t => {
  const dir = await directory(t), file = join(dir, 'test.local.json');
  await writeFile(file, JSON.stringify(await template('wechat')));
  const result = await loadDeploymentConfig(file, { env });
  assert.equal(result.configuration.authPath, join(dir, 'data', 'wechat-auth.sqlite'));
  await writeFile(file, 'x'.repeat(131073)); await assert.rejects(loadDeploymentConfig(file, { env }), { code: 'CONFIG.TOO_LARGE' });
  for (const content of ['{private-secret', Buffer.from([0xff])]) {
    await writeFile(file, content); await assert.rejects(loadDeploymentConfig(file, { env }), { code: 'CONFIG.UNREADABLE' });
  }
});

for (const platform of ['wechat', 'douyin', 'tiktok']) test(`${platform} configured server starts without external calls and uses only selected capabilities`, async t => {
  const dir = await directory(t), raw = await template(platform); raw.port = 0;
  const config = resolveDeploymentConfig(raw, { env, baseDir: dir });
  let requests = 0;
  const running = await startConfiguredServer(config, { log: () => {}, fetchImpl: async url => {
    requests++;
    if (platform === 'wechat') return Response.json({ openid: 'alice', session_key: 'private-key' });
    if (platform === 'douyin') return Response.json({ error: 0, openid: 'alice', session_key: 'private-key' });
    if (String(url).endsWith('/oauth/token/')) return Response.json({ open_id: 'alice', access_token: 'private-token', expires_in: 3600, token_type: 'Bearer' });
    return Response.json({ data: { trade_order_id: 'trade' }, error: { code: 'ok' } });
  } });
  t.after(() => running.stop()); assert.equal(requests, 0);
  const base = `http://127.0.0.1:${running.port}`;
  const post = async (path, body, session) => {
    const response = await fetch(base + path, { method: 'POST', headers: { 'Content-Type': 'application/json',
      ...(session ? { Authorization: 'Bearer ' + session } : {}) }, body: JSON.stringify(body) });
    return { status: response.status, data: await response.json() };
  };
  const login = await post('/v1/session', { ...config.configuration.binding, secret: undefined, code: 'login' });
  assert.equal(login.status, 200); assert.ok(login.data.session); assert.equal(requests, 1);
  const order = await post('/v1/orders', { productId: platform === 'douyin' ? 'sword' : 'coins', requestId: 'one' }, login.data.session);
  if (platform === 'wechat') assert.equal(order.status, 404);
  else {
    assert.equal(order.status, 200);
    const prepared = await post(`/v1/orders/${order.data.order.orderId}/prepare`, {}, login.data.session);
    assert.equal(prepared.status, 200); assert.ok(prepared.data.prepayment);
  }
  const stopping = running.stop(); assert.equal(stopping, running.stop()); await stopping;
});

test('CLI check emits safe preview and missing variable names without starting server', async t => {
  const dir = await directory(t), file = join(dir, 'check.json');
  await writeFile(file, JSON.stringify(await template('douyin')));
  const run = promisify(execFile), cli = fileURLToPath(new URL('../src/config-cli.mjs', import.meta.url));
  const { stdout, stderr } = await run(process.execPath, [cli, 'check', file], { env: { ...process.env, ...env } });
  assert.equal(JSON.parse(stdout).ready, true); assert.equal(stderr, '');
  for (const secret of Object.values(env)) assert.equal(stdout.includes(secret), false);
  await assert.rejects(run(process.execPath, [cli, 'check', file],
    { env: { ...process.env, ...env, DOUYIN_CALLBACK_TOKEN: '' } }), error => {
      assert.equal(error.code, 1); assert.deepEqual(JSON.parse(error.stdout).missingVariables, ['DOUYIN_CALLBACK_TOKEN']); return true;
    });
  assert.deepEqual(await readdir(dir), ['check.json']);
});
