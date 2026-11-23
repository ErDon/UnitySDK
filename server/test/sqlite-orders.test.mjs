import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { Worker } from 'node:worker_threads';
import { DatabaseSync } from 'node:sqlite';
import { SqliteOrderStore } from '../src/sqlite-orders.mjs';
import { OrderService } from '../src/orders.mjs';
const order=id=>({orderId:id,binding:'binding',subject:'user',productId:'coins',amountMinor:99,currency:'USD',state:'Created',createdAt:1000});
async function database(t) {
  const dir=await mkdtemp(join(tmpdir(),'erdon-order-test-'));const stores=[];
  t.after(async()=>{for(const store of stores)store.close();await rm(dir,{recursive:true,force:true});});
  const path=join(dir,'orders.sqlite');return {path,open:options=>{const store=new SqliteOrderStore(path,options);stores.push(store);return store;}};
}
test('orders, idempotency, paid delivery queue and fulfillment survive reopening',async t=>{
  const db=await database(t);let store=db.open();await store.create('request',order('one'));await store.markPaid('one','transaction');store.close();
  store=db.open();assert.equal((await store.pendingDelivery())[0].orderId,'one');
  assert.equal((await store.create('request',order('ignored-new-id'))).orderId,'one');
  await store.markFulfilled('one');store.close();store=db.open();
  assert.equal((await store.get('one')).state,'Fulfilled');assert.deepEqual(await store.pendingDelivery(),[]);
  await store.create('second',order('two'));
  await assert.rejects(store.markPaid('two','transaction'),{code:'PAY.TRANSACTION_REUSED'});
  await assert.rejects(store.markPaid('one','different'),{code:'PAY.TRANSACTION_CONFLICT'});
});
test('failed updates roll back, capacity preserves retries and closed stores fail safely',async t=>{
  const db=await database(t);const store=db.open({capacity:1});await store.create('request',order('one'));
  await assert.rejects(store.markFulfilled('one'),{code:'PAY.NOT_PAID'});
  await assert.rejects(store.create('request',{...order('two'),productId:'different'}),{code:'PAY.IDEMPOTENCY_CONFLICT'});
  await assert.rejects(store.create('other',order('two')),{code:'SERVER.CAPACITY'});
  assert.equal((await store.create('request',order('retry'))).orderId,'one');
  assert.equal((await store.get('one')).state,'Created');store.close();
  await assert.rejects(store.get('one'),{code:'SERVER.STORAGE_UNAVAILABLE'});
});
test('independent workers cannot claim one payment transaction for multiple orders',async t=>{
  const db=await database(t);const store=db.open();for(let i=0;i<4;i++)await store.create('request'+i,order('order'+i));
  const moduleUrl=new URL('../src/sqlite-orders.mjs',import.meta.url).href;
  const results=await Promise.all(Array.from({length:4},(_,i)=>new Promise((resolve,reject)=>{
    const worker=new Worker(`const {parentPort,workerData}=require('node:worker_threads');
      (async()=>{const {SqliteOrderStore}=await import(workerData.moduleUrl);const store=new SqliteOrderStore(workerData.path);
      try{await store.markPaid(workerData.id,'same-transaction');parentPort.postMessage('Paid');}
      catch(error){parentPort.postMessage(error.code);}finally{store.close();}})().catch(error=>{throw error;});`,
      {eval:true,workerData:{moduleUrl,path:db.path,id:'order'+i}});
    let result;worker.on('message',value=>{result=value;});worker.on('error',reject);
    worker.on('exit',code=>code===0?resolve(result):reject(new Error('Worker failed')));
  })));
  assert.equal(results.filter(x=>x==='Paid').length,1);assert.equal(results.filter(x=>x==='PAY.TRANSACTION_REUSED').length,3);
  assert.equal((await store.pendingDelivery()).length,1);
});
test('unknown order schema is refused without altering its version',async t=>{
  const db=await database(t);const raw=new DatabaseSync(db.path);
  raw.exec('CREATE TABLE erdon_order_schema (version INTEGER NOT NULL); INSERT INTO erdon_order_schema VALUES (99)');raw.close();
  assert.throws(()=>db.open(),/Cannot initialize order database/);
  const inspect=new DatabaseSync(db.path);assert.equal(inspect.prepare('SELECT version FROM erdon_order_schema').get().version,99);inspect.close();
});
test('order service resumes uncertain delivery after database restart with the same delivery key',async t=>{
  const db=await database(t);
  const binding={gameId:'game',appId:'app',platform:'WeChat',environment:'test'};
  const create=store=>new OrderService({binding,store,catalog:[{productId:'coins',amountMinor:99,currency:'USD'}],
    auth:{authenticate:async()=>({subject:'user'})},verifyPayment:async(b,o)=>({paid:true,orderId:o.orderId,
      appId:b.appId,platform:b.platform,subject:o.subject,amountMinor:o.amountMinor,currency:o.currency,transactionId:'receipt'})});
  let store=db.open();let service=create(store);
  const order=await service.create('session',{productId:'coins',requestId:'request'});await service.confirm('session',order.orderId,{});
  const delivered=new Set();let grants=0;
  await assert.rejects(service.fulfill(order.orderId,async message=>{
    delivered.add(message.deliveryKey);grants++;throw new Error('lost acknowledgement');
  }),{code:'PAY.DELIVERY_FAILED'});
  store.close();store=db.open();service=create(store);
  for(const pending of await store.pendingDelivery())await service.fulfill(pending.orderId,async message=>{
    assert.equal(message.deliveryKey,order.orderId);
    if(!delivered.has(message.deliveryKey)){delivered.add(message.deliveryKey);grants++;}return true;
  });
  assert.equal(grants,1);assert.equal((await service.get('session',order.orderId)).state,'Fulfilled');
});
