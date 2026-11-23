import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { AuthService, MemoryAuthStore } from '../src/auth.mjs';
import { OrderService, MemoryOrderStore } from '../src/orders.mjs';
import { createOrderHandler } from '../src/orders-http.mjs';
import { MemoryRateLimiter } from '../src/rate-limit.mjs';
async function fixture(t, options={}) {
  const binding={gameId:'game',appId:'app',platform:'WeChat',environment:'test',secret:'secret'};
  const auth=new AuthService({bindings:[binding],store:new MemoryAuthStore(),verifiers:{WeChat:async(b,c)=>({subject:c})}});
  const session=(await auth.exchange({...binding,code:'alice'})).session;
  const other=(await auth.exchange({...binding,code:'bob'})).session;
  let verifications=0;const logs=[];
  const service=new OrderService({binding,auth,store:new MemoryOrderStore(),catalog:[{productId:'coins',amountMinor:99,currency:'USD'}],
    verifyPayment:async(b,o,e)=>{verifications++;return e?.valid ? {paid:true,orderId:o.orderId,appId:b.appId,platform:b.platform,
      subject:o.subject,amountMinor:o.amountMinor,currency:o.currency,transactionId:'transaction'} : {paid:false};}});
  const server=createServer(createOrderHandler(service,{log:line=>logs.push(line),...options}));
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  t.after(()=>new Promise(resolve=>server.close(resolve)));
  const send=async(path,method='GET',body,token=session,headers={})=>{
    const response=await fetch(`http://127.0.0.1:${server.address().port}${path}`,{method,
      headers:{Authorization:`Bearer ${token}`,'Content-Type':'application/json',...headers},body:body===undefined?undefined:JSON.stringify(body)});
    return {status:response.status,headers:response.headers,data:await response.json()};
  };
  return {send,session,other,logs,auth,verifications:()=>verifications};
}
test('HTTP orders retain server prices, authenticate ownership, verify payment and never expose fulfillment',async t=>{
  const f=await fixture(t);const created=await f.send('/v1/orders','POST',{productId:'coins',requestId:'one',amountMinor:1});
  assert.equal(created.status,200);assert.equal(created.data.order.amountMinor,99);
  const path='/v1/orders/'+created.data.order.orderId;
  assert.equal((await f.send(path)).data.order.state,'Created');
  assert.equal((await f.send(path,'GET',undefined,f.other)).status,404);
  assert.equal((await f.send(path+'/confirm','POST',{paid:true})).data.errorCode,'PAY.NOT_PAID');
  const paid=await f.send(path+'/confirm','POST',{evidence:{valid:true}});
  assert.equal(paid.data.order.state,'Paid');assert.equal(f.verifications(),2);
  assert.equal((await f.send(path+'/fulfill','POST',{})).status,404);
  assert.equal(paid.headers.get('cache-control'),'no-store');
  assert.match(paid.data.callId,/^[0-9a-f-]{36}$/);
  assert.ok(!f.logs.join('').includes(f.session));assert.ok(!f.logs.join('').includes('valid'));
  await f.auth.revoke(f.session);assert.equal((await f.send(path)).status,401);
});
test('order HTTP rejects malformed bodies, excessive evidence and incorrect authentication',async t=>{
  const f=await fixture(t,{bodyLimit:128});
  assert.equal((await f.send('/v1/orders','POST',{} ,'bad')).status,401);
  for(const body of [null,[],42])assert.equal((await f.send('/v1/orders','POST',body)).status,400);
  assert.equal((await f.send('/v1/orders','POST',{productId:'coins',requestId:'x'.repeat(200)})).status,413);
  assert.equal((await f.send('/v1/orders','POST',{},f.session,{'Content-Type':'text/plain'})).status,415);
  assert.equal((await f.send('/v1/orders?session=secret','POST',{})).status,404);
});
test('order HTTP limiter prevents extra requests and log callback exceptions do not change results',async t=>{
  const f=await fixture(t,{limiter:new MemoryRateLimiter({limit:1}),log:()=>{throw new Error('log failure');}});
  assert.equal((await f.send('/v1/orders','POST',{productId:'coins',requestId:'one'})).status,200);
  const limited=await f.send('/v1/orders','POST',{});
  assert.equal(limited.status,429);assert.equal(limited.data.errorCode,'PAY.RATE_LIMITED');assert.ok(Number(limited.headers.get('retry-after'))>0);
});
