import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { AuthService, MemoryAuthStore, digest } from '../src/auth.mjs';
import { createWeChatVerifier } from '../src/wechat.mjs';
import { createAuthHandler } from '../src/http.mjs';

const binding = { gameId: 'game-a', appId: 'wx-app', environment: 'test', platform: 'WeChat', secret: 'server-secret' };
const request = (code = 'temporary-code') => ({ ...binding, secret: undefined, code });
const rejectsCode = (promise, code) => assert.rejects(promise, error => error.code === code);
function setup(verify = async () => ({ subject: 'user-id' })) {
  let clock = 1000;
  const now = () => clock;
  const store = new MemoryAuthStore({ now });
  const service = new AuthService({ bindings: [binding], verifiers: { WeChat: verify }, store, now, sessionTtlMs: 500 });
  return { service, store, advance: () => { clock += 501; } };
}
test('server sessions are hashed, scoped, revocable and expire', async () => {
  const { service, store, advance } = setup();
  const { session } = await service.exchange(request());
  assert.notEqual(session, 'temporary-code'); assert.notEqual(session, 'user-id');
  assert.ok(store.sessions.has(digest(session))); assert.ok(!store.sessions.has(session));
  assert.equal((await service.authenticate(session, binding)).subject, 'user-id');
  await rejectsCode(service.authenticate(session, { ...binding, gameId: 'other' }), 'AUTH.SESSION_INVALID');
  await rejectsCode(service.authenticate(session, { ...binding, environment: 'production' }), 'AUTH.SESSION_INVALID');
  await service.revoke(session);
  await rejectsCode(service.authenticate(session, binding), 'AUTH.SESSION_INVALID');
  const next = await service.exchange(request('code-2')); advance();
  await rejectsCode(service.authenticate(next.session, binding), 'AUTH.SESSION_INVALID');
});
test('untrusted application and platform rejected before verification', async () => {
  let calls=0; const { service }=setup(async()=>{ calls++; return { subject:'id' }; });
  await rejectsCode(service.exchange({ ...request(), appId: 'attacker-app' }), 'AUTH.APP_NOT_ALLOWED');
  await rejectsCode(service.exchange({ ...request(), platform: '__proto__' }), 'AUTH.APP_NOT_ALLOWED');
  await rejectsCode(service.exchange({ ...request(), code: '' }), 'REQUEST.INVALID');
  assert.equal(calls,0);
});
test('atomic claim blocks concurrent replay and bad identities never create sessions', async () => {
  let calls=0; const { service, store }=setup(async()=>{calls++;return {};});
  const results=await Promise.allSettled([service.exchange(request()),service.exchange(request())]);
  assert.deepEqual(results.map(r=>r.reason.code).sort(), ['AUTH.CODE_REUSED','AUTH.INVALID_IDENTITY']);
  assert.equal(calls,1); assert.equal(store.sessions.size,0);
});
test('WeChat request is fixed, redirect-disabled and only validated subject escapes', async () => {
  const verify=createWeChatVerifier({fetchImpl:async(url,options)=>{
    assert.equal(url.origin,'https://api.weixin.qq.com'); assert.equal(url.pathname,'/sns/jscode2session');
    assert.equal(url.searchParams.get('secret'),'server-secret'); assert.equal(url.searchParams.get('js_code'),'temporary-code');
    assert.equal(options.redirect,'error'); assert.ok(options.signal);
    return Response.json({openid:'id',session_key:'private-key',unionid:'other-id'});
  }});
  assert.deepEqual(await verify(binding,'temporary-code'),{subject:'id'});
});
test('WeChat rejects malformed/oversize/error responses and sanitizes network errors', async () => {
  for (const response of [Response.json({errcode:40029,errmsg:'secret'}),Response.json({openid:'id'}),new Response('{'),new Response('x'.repeat(17000))]) {
    const verify=createWeChatVerifier({fetchImpl:async()=>response});
    await assert.rejects(verify(binding,'code'),error=>error.code.startsWith('AUTH.PLATFORM_')&&!error.message.includes('secret'));
  }
  await rejectsCode(createWeChatVerifier({fetchImpl:async()=>{throw new Error('secret URL');}})(binding,'code'),'AUTH.PLATFORM_UNAVAILABLE');
});
test('HTTP integration returns safe protocol, bounds request bodies and isolates log sink', async t => {
  const { service }=setup(); const logs=[];
  const server=createServer(createAuthHandler(service,{log:line=>{logs.push(line);throw new Error('sink');}}));
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  t.after(()=>new Promise(resolve=>server.close(resolve)));
  const url=`http://127.0.0.1:${server.address().port}/v1/session`;
  const post=body=>fetch(url,{method:'POST',headers:{'Content-Type':'application/json'},body});
  const response=await post(JSON.stringify(request())); const body=await response.json();
  assert.equal(response.status,200); assert.ok(body.session); assert.ok(body.callId);
  assert.equal(response.headers.get('cache-control'),'no-store');
  const replay=await post(JSON.stringify(request())); assert.equal(replay.status,409); await replay.text();
  const malformed=await post('{'); assert.equal(malformed.status,400); await malformed.text();
  const large=await post('x'.repeat(9000)); assert.equal(large.status,413); await large.text();
  for(const line of logs) {
    assert.ok(!line.includes('temporary-code')&&!line.includes('server-secret')&&!line.includes(body.session));
    for(const field of ['time','interface','callId','logId','params','stage','result']) assert.ok(JSON.parse(line)[field]);
  }
});
