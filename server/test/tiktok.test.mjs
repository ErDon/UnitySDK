import test from 'node:test';
import assert from 'node:assert/strict';
import { createTikTokVerifier } from '../src/tiktok.mjs';
import { AuthService, MemoryAuthStore } from '../src/auth.mjs';

const binding = { gameId:'game', appId:'client-key', secret:'private-secret', environment:'test', platform:'TikTok' };
const success = { open_id:'account', access_token:'private-token', token_type:'Bearer', expires_in:86400,
  refresh_token:'private-refresh', refresh_expires_in:31536000 };
test('TikTok Minis exchange uses form POST and preserves SDK code without double decoding', async()=>{
  const code='literal+%2F/&=?';
  const verify=createTikTokVerifier({fetchImpl:async(url,options)=>{
    assert.equal(url,'https://open.tiktokapis.com/v2/oauth/token/');
    assert.equal(options.method,'POST'); assert.equal(options.redirect,'error'); assert.ok(options.signal);
    assert.equal(options.headers['Content-Type'],'application/x-www-form-urlencoded');
    assert.deepEqual(Object.fromEntries(new URLSearchParams(options.body)),{
      client_key:'client-key',client_secret:'private-secret',code,grant_type:'authorization_code'
    });
    return Response.json(success);
  }});
  assert.deepEqual(await verify(binding,code),{subject:'account'});
});
test('TikTok rejects malformed or contradictory identity envelopes', async()=>{
  for(const data of [null,[],{}, {...success,error:''},{...success,error:'invalid_request'},
    {...success,open_id:' '},{...success,access_token:''},{...success,expires_in:'86400'},
    {...success,expires_in:0},{...success,token_type:'other'}]) {
    await assert.rejects(createTikTokVerifier({fetchImpl:async()=>Response.json(data)})(binding,'code'));
  }
});
test('TikTok upstream failures remain safe and bounded', async()=>{
  for(const status of [429,500,503])
    await assert.rejects(createTikTokVerifier({fetchImpl:async()=>new Response('private-token',{status})})(binding,'code'),
      {code:'AUTH.PLATFORM_UNAVAILABLE'});
  await assert.rejects(createTikTokVerifier({fetchImpl:async()=>Response.json({error:'invalid_request',error_description:'private-secret'},{status:400})})(binding,'code'),
    {code:'AUTH.PLATFORM_REJECTED',message:'AUTH.PLATFORM_REJECTED'});
  await assert.rejects(createTikTokVerifier({fetchImpl:async()=>new Response('x'.repeat(17000))})(binding,'code'),{code:'AUTH.PLATFORM_RESPONSE_INVALID'});
  await assert.rejects(createTikTokVerifier({fetchImpl:async()=>{throw new Error('private-secret');}})(binding,'code'),{code:'AUTH.PLATFORM_UNAVAILABLE'});
});
test('TikTok identity creates scoped game session without returning platform tokens', async()=>{
  const service=new AuthService({bindings:[binding],store:new MemoryAuthStore(),verifiers:{
    TikTok:createTikTokVerifier({fetchImpl:async()=>Response.json(success)})
  }});
  const result=await service.exchange({...binding,code:'temporary-code'});
  assert.equal((await service.authenticate(result.session,binding)).subject,'account');
  assert.ok(!JSON.stringify(result).includes('private-'));
  await assert.rejects(service.exchange({...binding,code:'temporary-code'}),{code:'AUTH.CODE_REUSED'});
});
