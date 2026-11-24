import test from 'node:test';
import assert from 'node:assert/strict';
import { createTikTokTokenClient } from '../src/tiktok-token-client.mjs';
const binding={platform:'TikTok',appId:'app',secret:'private-secret'};
const tokens={open_id:'player',access_token:'access-new',refresh_token:'refresh-new',token_type:'Bearer',expires_in:60,refresh_expires_in:3600};
test('refresh preserves encoded token, verifies user and returns rotated token with conservative expiry',async()=>{
  let clock=1000;
  const client=createTikTokTokenClient({now:()=>clock,fetchImpl:async(url,options)=>{
    assert.equal(url,'https://open.tiktokapis.com/v2/oauth/token/');assert.equal(options.redirect,'error');assert.ok(options.signal);
    assert.deepEqual(Object.fromEntries(new URLSearchParams(options.body)),{client_key:'app',client_secret:'private-secret',grant_type:'refresh_token',refresh_token:'literal+%2f'});
    clock=2000;return Response.json(tokens);
  }});
  assert.deepEqual(await client.refresh(binding,'player','literal+%2f'),{subject:'player',accessToken:'access-new',expiresAt:61000,refreshToken:'refresh-new',refreshExpiresAt:3601000});
});
test('refresh rejects identity changes, missing rotation fields, invalid expiry and errors without leaking tokens',async()=>{
  for(const patch of [{open_id:'other'},{refresh_token:''},{expires_in:0},{refresh_expires_in:Number.MAX_SAFE_INTEGER},{error:'private-upstream'}]) {
    await assert.rejects(createTikTokTokenClient({fetchImpl:async()=>Response.json({...tokens,...patch})}).refresh(binding,'player','refresh-old'),{code:'AUTH.PLATFORM_RESPONSE_INVALID'});
  }
  for(const status of [400,401,429,500])
    await assert.rejects(createTikTokTokenClient({fetchImpl:async()=>new Response('private-token',{status})}).refresh(binding,'player','refresh'),
      {code:status<429?'AUTH.PLATFORM_REJECTED':'AUTH.PLATFORM_UNAVAILABLE'});
});
test('revoke accepts only empty success and uses fixed authenticated form endpoint',async()=>{
  for(const body of ['', '{}']) {
    const client=createTikTokTokenClient({fetchImpl:async(url,options)=>{
      assert.equal(url,'https://open.tiktokapis.com/v2/oauth/revoke/');
      assert.deepEqual(Object.fromEntries(new URLSearchParams(options.body)),{client_key:'app',client_secret:'private-secret',token:'access'});
      return new Response(body);
    }});
    await client.revoke(binding,'access');
  }
  for(const body of ['null','[]','{"error":"private"}','x'.repeat(17000)])
    await assert.rejects(createTikTokTokenClient({fetchImpl:async()=>new Response(body)}).revoke(binding,'access'),{code:'AUTH.PLATFORM_RESPONSE_INVALID'});
});
