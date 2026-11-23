import test from 'node:test';
import assert from 'node:assert/strict';
import { createDouyinVerifier } from '../src/douyin.mjs';
import { AuthService, MemoryAuthStore } from '../src/auth.mjs';

const binding = { gameId:'game', appId:'tt-app', platform:'Douyin', environment:'test', secret:'private-secret' };
const verifyResponse = data => createDouyinVerifier({fetchImpl:async()=>Response.json(data)})(binding,'code');
test('Douyin uses the mini-game endpoint and only returns the verified account',async()=>{
  const verify=createDouyinVerifier({fetchImpl:async(url,options)=>{
    assert.equal(url.origin,'https://minigame.zijieapi.com');
    assert.equal(url.pathname,'/mgplatform/api/apps/jscode2session');
    assert.deepEqual(Object.fromEntries(url.searchParams),{appid:'tt-app',secret:'private-secret',code:'temporary-code'});
    assert.equal(options.method,'GET');assert.equal(options.redirect,'error');assert.ok(options.signal);
    assert.equal(options.headers['Content-Type'],'application/json');
    return Response.json({error:0,openid:'account',session_key:'private-key',anonymous_openid:'anonymous',unionid:'other'});
  }});
  assert.deepEqual(await verify(binding,'temporary-code'),{subject:'account'});
});
test('Douyin rejects anonymous-only, ambiguous success and malformed envelopes',async()=>{
  for(const data of [null,[],{}, {error:'0',openid:'id',session_key:'key'}, {error:0,anonymous_openid:'anon'},
    {error:0,openid:'id',session_key:''},{error:0,errcode:40018,openid:'id',session_key:'key'}])
    await assert.rejects(verifyResponse(data),{code:'AUTH.PLATFORM_RESPONSE_INVALID'});
});
test('documented configuration errors are distinguished from rejected credentials',async()=>{
  for(const errcode of [40014,40015,40017])
    await assert.rejects(verifyResponse({error:1,errcode,errmsg:'private-secret'}),{code:'AUTH.PLATFORM_CONFIGURATION'});
  await assert.rejects(verifyResponse({error:40018,message:'sensitive'}),{code:'AUTH.PLATFORM_REJECTED'});
  await assert.rejects(verifyResponse({error:-1}),{code:'AUTH.PLATFORM_UNAVAILABLE'});
});
test('transport errors and oversized bodies never expose upstream secrets',async()=>{
  for(const fetchImpl of [async()=>{throw new Error('URL with private-secret');},async()=>new Response('private-secret',{status:503})])
    await assert.rejects(createDouyinVerifier({fetchImpl})(binding,'code'),{code:'AUTH.PLATFORM_UNAVAILABLE'});
  await assert.rejects(createDouyinVerifier({fetchImpl:async()=>new Response('x'.repeat(17000))})(binding,'code'),{code:'AUTH.PLATFORM_RESPONSE_INVALID'});
});
test('platform binding selects Douyin verifier and issues only a game session',async()=>{
  let calls=0;
  const service=new AuthService({bindings:[binding],store:new MemoryAuthStore(),verifiers:{
    WeChat:async()=>{throw new Error('wrong verifier');},
    Douyin:createDouyinVerifier({fetchImpl:async()=>{calls++;return Response.json({error:0,openid:'account',session_key:'private-key'});}})
  }});
  const result=await service.exchange({...binding,code:'code'});
  assert.equal(calls,1);assert.equal((await service.authenticate(result.session,binding)).subject,'account');
  assert.ok(!JSON.stringify(result).includes('private-key'));
  await assert.rejects(service.exchange({...binding,platform:'WeChat',code:'code2'}),{code:'AUTH.APP_NOT_ALLOWED'});
});
