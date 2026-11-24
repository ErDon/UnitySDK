import test from 'node:test';
import assert from 'node:assert/strict';
import { OrderService, MemoryOrderStore } from '../src/orders.mjs';
import { DeliveryWorker } from '../src/delivery-worker.mjs';

async function fixture() {
  const store=new MemoryOrderStore();
  const binding={gameId:'game',appId:'app',platform:'TikTok',environment:'test'};
  const orders=new OrderService({binding,store,catalog:[{productId:'coins',amountMinor:99,currency:'USD'}],
    auth:{authenticate:async()=>({subject:'private-player'})},verifyPayment:async()=>({paid:false})});
  const ids=[];
  for(let i=0;i<3;i++) {
    const order=await orders.create('session',{productId:'coins',requestId:String(i)});
    await store.markPaid(order.orderId,'transaction-'+i);ids.push(order.orderId);
  }
  return {orders,store,ids};
}
test('worker retries failed delivery, rotates batches and never logs receiver secrets',async()=>{
  const {orders,store,ids}=await fixture(), logs=[],delivered=[];
  const worker=new DeliveryWorker({orders,batchSize:1,log:line=>logs.push(line),deliver:async message=>{
    if(message.deliveryKey===ids[0])throw new Error('private receiver secret');
    delivered.push(message.deliveryKey);return true;
  }});
  await worker.runOnce();await worker.runOnce();await worker.runOnce();await worker.runOnce();
  assert.deepEqual(new Set(delivered),new Set(ids.slice(1)));
  assert.equal((await store.get(ids[0])).state,'Paid');
  assert.equal((await store.get(ids[1])).state,'Fulfilled');
  assert.ok(!logs.join('').includes('private'));
  assert.ok(logs.every(line=>JSON.parse(line).callId&&JSON.parse(line).logId));
  await worker.stop();
});
test('worker coalesces concurrent drains and stop waits for active delivery without starting another',async()=>{
  const {orders,store,ids}=await fixture();let release,entered;const started=new Promise(resolve=>entered=resolve);let calls=0;
  const worker=new DeliveryWorker({orders,log:()=>{},deliver:async()=>{calls++;entered();await new Promise(resolve=>release=resolve);return true;}});
  const first=worker.runOnce();assert.equal(first,worker.runOnce());await started;
  let stopped=false;const stopping=worker.stop().then(()=>stopped=true);await Promise.resolve();assert.equal(stopped,false);
  release();await first;await stopping;await worker.runOnce();
  assert.equal(calls,1);assert.equal((await store.get(ids[0])).state,'Fulfilled');
  assert.equal((await store.pendingDelivery()).length,2);
  assert.throws(()=>worker.start(),/stopped/);
});
test('worker ignores other application scopes and storage/log errors remain contained',async()=>{
  const {orders,store}=await fixture();let calls=0;
  for(const order of store.orders.values())order.binding='other-app';
  const worker=new DeliveryWorker({orders,deliver:async()=>{calls++;return true;},log:()=>{throw new Error('log');}});
  await worker.runOnce();assert.equal(calls,0);
  store.pendingDelivery=async()=>{throw new Error('private database');};
  await worker.runOnce();await worker.stop();
});
test('started worker schedules delivery and stop cancels future runs',async()=>{
  const {orders}=await fixture();let calls=0,notify;
  const delivered=new Promise(resolve=>notify=resolve);
  const worker=new DeliveryWorker({orders,intervalMs:100,batchSize:1,log:()=>{},deliver:async()=>{calls++;notify();return true;}});
  // Keep this standalone test alive; production HTTP listener keeps its worker alive.
  const deadline=setTimeout(()=>notify(),2000);
  try {worker.start();worker.start();await delivered;await worker.stop();assert.equal(calls,1);assert.equal(worker.timer,null);}
  finally{clearTimeout(deadline);await worker.stop();}
});
