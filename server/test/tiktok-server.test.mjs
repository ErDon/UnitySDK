import test from 'node:test';
import assert from 'node:assert/strict';
import { startTikTokServer } from '../src/tiktok-server.mjs';
const configuration=()=>({binding:{gameId:'game',appId:'app',environment:'test',platform:'TikTok',secret:'secret'},
  isSandbox:true,authPath:':memory:',orderPath:':memory:',credentialPath:':memory:',
  keys:new Map([['key',Buffer.alloc(32,9)]]),currentKeyId:'key',log:()=>{},
  catalog:[{productId:'coins',amountMinor:99,currency:'USD',tokenAmount:100,productName:'Coins',quantity:1,quantityUnit:'item',imageUrl:'https://example.com/item.png'}]});
test('loopback server runs composed handler, handles occupied port, and stops idempotently',async t=>{
  const running=await startTikTokServer(configuration(),{port:0});t.after(()=>running.stop());
  const response=await fetch(`http://127.0.0.1:${running.port}/v1/orders`);
  assert.equal(response.status,404);assert.equal((await response.json()).errorCode,'REQUEST.NOT_FOUND');
  await assert.rejects(startTikTokServer(configuration(),{port:running.port}),{message:'TikTok server could not start'});
  const first=running.stop();assert.equal(first,running.stop());await first;
  const restarted=await startTikTokServer(configuration(),{port:running.port});await restarted.stop();
});
