import test from 'node:test';
import assert from 'node:assert/strict';
import { OrderService, MemoryOrderStore } from '../src/orders.mjs';
import { AuthService, MemoryAuthStore } from '../src/auth.mjs';
const binding={gameId:'game',appId:'app',environment:'test',platform:'WeChat'};
const catalog=[{productId:'coins',amountMinor:199,currency:'USD'},{productId:'gems',amountMinor:299,currency:'USD'}];
const paid=(b,o)=>({paid:true,orderId:o.orderId,appId:b.appId,platform:b.platform,subject:o.subject,
  amountMinor:o.amountMinor,currency:o.currency,transactionId:'transaction-'+o.orderId});
function fixture(options={}) {
  const store=options.store??new MemoryOrderStore();
  const service=new OrderService({binding,catalog,store,auth:{authenticate:async(session)=>{
    if(!['alice','bob'].includes(session))throw new Error('unauthorized');return {subject:session};
  }},verifyPayment:async(b,o)=>paid(b,o),...options});
  return {service,store};
}
test('order prices come from catalog and idempotency is scoped to user and binding',async()=>{
  const {service,store}=fixture();
  const request={productId:'coins',requestId:'one',amountMinor:1,currency:'XXX'};
  const [a,b]=await Promise.all([service.create('alice',request),service.create('alice',request)]);
  assert.equal(a.orderId,b.orderId);assert.equal(a.amountMinor,199);assert.equal(a.currency,'USD');
  await assert.rejects(service.create('alice',{...request,productId:'gems'}),{code:'PAY.IDEMPOTENCY_CONFLICT'});
  const other=await service.create('bob',request);assert.notEqual(a.orderId,other.orderId);
  await assert.rejects(service.get('bob',a.orderId),{code:'PAY.ORDER_NOT_FOUND'});
  const alternate=fixture({store,binding:{...binding,gameId:'other'}}).service;
  await assert.rejects(alternate.get('alice',a.orderId),{code:'PAY.ORDER_NOT_FOUND'});
  assert.notEqual((await alternate.create('alice',request)).orderId,a.orderId);
  assert.equal(a.subject,undefined);
});
test('client success is never trusted and every payment identity field must match',async()=>{
  for(const changed of [{paid:false},{orderId:'other'},{appId:'other'},{platform:'other'},
    {subject:'bob'},{amountMinor:1},{currency:'CNY'},{transactionId:''}]) {
    const {service,store}=fixture({verifyPayment:async(b,o)=>({...paid(b,o),...changed})});
    const order=await service.create('alice',{productId:'coins',requestId:'one'});
    await assert.rejects(service.confirm('alice',order.orderId,{paid:true}));
    assert.equal((await service.get('alice',order.orderId)).state,'Created');
    assert.deepEqual(await store.pendingDelivery(),[]);
    await assert.rejects(service.fulfill(order.orderId,async()=>true),{code:'PAY.NOT_PAID'});
  }
});
test('concurrent confirmation creates one paid order and one transaction cannot fund two orders',async()=>{
  const {service,store}=fixture({verifyPayment:async(b,o)=>({...paid(b,o),transactionId:'same'})});
  const first=await service.create('alice',{productId:'coins',requestId:'one'});
  await Promise.all([service.confirm('alice',first.orderId,{}),service.confirm('alice',first.orderId,{})]);
  assert.equal((await store.pendingDelivery()).length,1);
  const second=await service.create('alice',{productId:'coins',requestId:'two'});
  await assert.rejects(service.confirm('alice',second.orderId,{}),{code:'PAY.TRANSACTION_REUSED'});
  assert.equal((await service.get('alice',second.orderId)).state,'Created');
});
test('delivery retries keep the same key and receiver dedup prevents repeated grants after lost acknowledgement',async()=>{
  const {service,store}=fixture();const order=await service.create('alice',{productId:'coins',requestId:'one'});
  await service.confirm('alice',order.orderId,{});
  const delivered=new Set();let grants=0;let attempts=0;
  const deliver=async message=>{
    attempts++;if(!delivered.has(message.deliveryKey)){delivered.add(message.deliveryKey);grants++;}
    if(attempts===1)throw new Error('ack lost');return true;
  };
  await assert.rejects(service.fulfill(order.orderId,deliver),{code:'PAY.DELIVERY_FAILED'});
  assert.equal((await store.pendingDelivery()).length,1);
  assert.equal((await service.fulfill(order.orderId,deliver)).state,'Fulfilled');
  await service.fulfill(order.orderId,deliver);
  assert.equal(grants,1);assert.equal(attempts,2);assert.deepEqual(await store.pendingDelivery(),[]);
});
test('concurrent local delivery is serialized; failure does not mark fulfilled',async()=>{
  const {service}=fixture();const order=await service.create('alice',{productId:'coins',requestId:'one'});
  await service.confirm('alice',order.orderId,{});
  let release;const wait=new Promise(resolve=>{release=resolve;});
  const attempt=service.fulfill(order.orderId,async()=>{await wait;return false;});
  await assert.rejects(service.fulfill(order.orderId,async()=>true),{code:'PAY.DELIVERY_IN_PROGRESS'});
  release();await assert.rejects(attempt,{code:'PAY.DELIVERY_FAILED'});
  assert.equal((await service.get('alice',order.orderId)).state,'Paid');
});
test('verification exceptions are sanitized and storage is bounded',async()=>{
  const {service}=fixture({store:new MemoryOrderStore({capacity:1}),verifyPayment:async()=>{throw new Error('secret receipt');}});
  const order=await service.create('alice',{productId:'coins',requestId:'one'});
  await assert.rejects(service.confirm('alice',order.orderId,{}),{code:'PAY.VERIFICATION_FAILED',message:'PAY.VERIFICATION_FAILED'});
  await assert.rejects(service.create('alice',{productId:'coins',requestId:'two'}),{code:'SERVER.CAPACITY'});
  assert.equal((await service.create('alice',{productId:'coins',requestId:'one'})).orderId,order.orderId);
});
test('orders use verified game sessions and reject revoked or cross-environment sessions',async()=>{
  const other={...binding,environment:'production'};
  const auth=new AuthService({bindings:[{...binding,secret:'test'},{...other,secret:'prod'}],store:new MemoryAuthStore(),
    verifiers:{WeChat:async()=>({subject:'verified-account'})}});
  const session=(await auth.exchange({...binding,code:'test-code'})).session;
  const foreign=(await auth.exchange({...other,code:'prod-code'})).session;
  const {service}=fixture({auth});
  const order=await service.create(session,{productId:'coins',requestId:'one',subject:'forged-account'});
  await assert.rejects(service.get(foreign,order.orderId),{code:'AUTH.SESSION_INVALID'});
  await auth.revoke(session);
  await assert.rejects(service.confirm(session,order.orderId,{paid:true}),{code:'AUTH.SESSION_INVALID'});
});
