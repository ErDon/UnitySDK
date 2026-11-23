import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { MemoryRateLimiter } from '../src/rate-limit.mjs';
import { createAuthHandler } from '../src/http.mjs';

test('limits requests, reports exact remaining window and expires entries',()=>{
  let time=1000; const limiter=new MemoryRateLimiter({limit:2,windowMs:2500,now:()=>time});
  assert.equal(limiter.take('source').allowed,true);assert.equal(limiter.take('source').allowed,true);
  assert.deepEqual(limiter.take('source'),{allowed:false,retryAfterSeconds:3});
  time=3000;assert.equal(limiter.take('source').retryAfterSeconds,1);
  time=3500;assert.equal(limiter.take('source').allowed,true);
  assert.equal(limiter.take('other').allowed,true);
  assert.ok(!JSON.stringify([...limiter.entries]).includes('source'));
});
test('bounded key storage fails closed, then reuses expired capacity',()=>{
  let time=0;const limiter=new MemoryRateLimiter({limit:1,windowMs:1000,maxKeys:1,now:()=>time});
  limiter.take('a');assert.throws(()=>limiter.take('b'),{code:'SERVER.CAPACITY'});
  time=1000;assert.equal(limiter.take('b').allowed,true);
  assert.throws(()=>new MemoryRateLimiter({limit:0}),TypeError);
});
test('HTTP limit runs before verification, ignores spoofed forwarding header, and supplies Retry-After',async t=>{
  let exchanges=0;const logs=[];
  const server=createServer(createAuthHandler({exchange:async()=>{exchanges++;return {session:'test-session'};}}, {
    limiter:new MemoryRateLimiter({limit:1,windowMs:60000}),log:line=>logs.push(line)
  }));
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  t.after(()=>new Promise(resolve=>server.close(resolve)));
  const url=`http://127.0.0.1:${server.address().port}/v1/session`;
  const send=forwarded=>fetch(url,{method:'POST',headers:{'Content-Type':'application/json','X-Forwarded-For':forwarded},body:'{}'});
  const first=await send('192.0.2.1');assert.equal(first.status,200);await first.text();
  const second=await send('192.0.2.2');assert.equal(second.status,429);
  assert.equal((await second.json()).errorCode,'AUTH.RATE_LIMITED');
  assert.ok(Number(second.headers.get('retry-after'))>0);assert.equal(exchanges,1);
  assert.ok(!logs.join('').includes('192.0.2.'));assert.ok(!logs.join('').includes('127.0.0.1'));
});
