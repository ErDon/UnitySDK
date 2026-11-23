import test from 'node:test';
import assert from 'node:assert/strict';
import { createTikTokVerifier } from '../src/tiktok.mjs';
import { AuthService, MemoryAuthStore } from '../src/auth.mjs';
import { TikTokCredentialStore } from '../src/tiktok-credential-store.mjs';

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

test('TikTok saves scoped encrypted credentials before issuing session, with conservative expiry', async t=>{
  let clock=100000;
  const credentials=new TikTokCredentialStore(':memory:',{keys:new Map([['test',Buffer.alloc(32,7)]]),currentKeyId:'test',now:()=>clock});
  t.after(()=>credentials.close());
  const store=new MemoryAuthStore();
  const putSession=store.putSession.bind(store);
  store.putSession=async(...args)=>{
    const saved=await credentials.get(binding,'account');
    assert.equal(saved.accessToken,success.access_token);
    assert.equal(saved.expiresAt,100000+86400000);
    await putSession(...args);
  };
  const service=new AuthService({bindings:[binding],store,verifiers:{TikTok:createTikTokVerifier({
    credentialStore:credentials,now:()=>clock,fetchImpl:async()=>{clock+=5000;return Response.json(success);}
  })}});
  const result=await service.exchange({...binding,code:'commerce-code'});
  assert.equal((await service.authenticate(result.session,binding)).subject,'account');
  assert.deepEqual(Object.keys(result).sort(),['expiresAt','session']);
  assert.ok(!JSON.stringify(result).includes('private-'));
  assert.equal(await credentials.get({...binding,gameId:'other'},'account'),null);
});

test('TikTok storage failure is sanitized and issues no session; consumed code stays claimed', async()=>{
  const store=new MemoryAuthStore();
  const service=new AuthService({bindings:[binding],store,verifiers:{TikTok:createTikTokVerifier({
    credentialStore:{put:async()=>{throw new Error('private-token database path');}},
    fetchImpl:async()=>Response.json(success)
  })}});
  const request={...binding,code:'failed-save'};
  await assert.rejects(service.exchange(request),{code:'AUTH.CREDENTIAL_STORAGE_FAILED',status:503,message:'AUTH.CREDENTIAL_STORAGE_FAILED'});
  assert.equal(store.sessions.size,0);
  await assert.rejects(service.exchange(request),{code:'AUTH.CODE_REUSED'});
});

test('TikTok rejects unusable expiry before writing commerce credentials', async()=>{
  for(const [start,end,seconds] of [[1000,2000,1],[1000,999,60],[1000,1000,Number.MAX_SAFE_INTEGER]]) {
    let clock=start,writes=0;
    const verify=createTikTokVerifier({now:()=>clock,credentialStore:{put:async()=>{writes++;}},
      fetchImpl:async()=>{clock=end;return Response.json({...success,expires_in:seconds});}});
    await assert.rejects(verify(binding,'code'),{code:'AUTH.PLATFORM_RESPONSE_INVALID'});
    assert.equal(writes,0);
  }
});
