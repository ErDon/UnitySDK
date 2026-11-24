import test from 'node:test';
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { mkdtemp,writeFile,readFile,rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { createOperatorEvidenceVerifier, signOperatorEvidence } from '../src/operator-evidence.mjs';
import { PaymentRecovery } from '../src/payment-recovery.mjs';
import { SqliteOrderStore } from '../src/sqlite-orders.mjs';
import { AuthService,MemoryAuthStore } from '../src/auth.mjs';
import { OrderService } from '../src/orders.mjs';
import { DouyinPayments } from '../src/douyin-payments.mjs';
import { WeChatPayments } from '../src/wechat-payments.mjs';
import { TikTokPrepayments } from '../src/tiktok-prepayments.mjs';
import { TikTokPrepaymentFlow } from '../src/tiktok-prepayment-flow.mjs';

const key=Buffer.alloc(32,7),now=1800000000000;
const verify=createOperatorEvidenceVerifier({keys:new Map([['operator',key]]),now:()=>now});
const sign=value=>signOperatorEvidence(value,'operator',key);
const metadata={schemaVersion:1,evidenceId:'evidence-1',reviewer:'operator-A',sourceReference:'platform-case-123',sourceSha256:'a'.repeat(64),issuedAt:now,expiresAt:now+60000};
for(const platform of ['WeChat','Douyin','TikTok'])test(`${platform} reviewed payment recovery previews, checks immutable preparation and commits audit atomically`,async t=>{
  const binding={platform,appId:'app',gameId:'game',environment:'test',secret:'secret'};
  const store=new SqliteOrderStore(':memory:');t.after(()=>store.close());
  const auth=new AuthService({bindings:[binding],store:new MemoryAuthStore(),verifiers:{[platform]:async()=>({subject:'alice'})}});
  const session=(await auth.exchange({...binding,code:'code'})).session;
  const catalog=[{productId:'sword',productName:'Sword',amountMinor:600,currency:'CNY'}];
  const orders=new OrderService({binding,store,auth,catalog,verifyPayment:async()=>({paid:false})});
  const order=await orders.create(session,{productId:'sword',requestId:'first'});
  let platformOrder=order.orderId;
  if(platform==='WeChat') {
    const payments=new WeChatPayments({orders,credentialStore:{get:async()=>({sessionKey:'key'})},offerId:'offer',zoneId:'1',appKey:'pay',originalId:'gh_app',isSandbox:true});
    platformOrder=JSON.parse((await payments.prepare(session,order.orderId)).signData).outTradeNo;
  }else if(platform==='Douyin')await new DouyinPayments({orders,catalog}).prepare(session,order.orderId);
  else {
    const payments=new TikTokPrepayments(store),flow=new TikTokPrepaymentFlow(payments,{create:async()=>{throw new Error('response lost');}});
    await assert.rejects(flow.create({binding,isSandbox:true,openId:'alice',accessToken:'token',orderId:order.orderId,tokenAmount:100,
      productName:'Sword',orderUrl:'/orders/'+order.orderId,quantity:1,quantityUnit:'item',imageUrl:'https://example.com/image.png'}));
    platformOrder='trade-1';
  }
  const recovery=new PaymentRecovery({binding,store,verifyEvidence:verify,now:()=>now});
  let evidence={...metadata,...binding,subject:'alice',orderId:order.orderId,productId:'sword',amountMinor:600,currency:'CNY',
    kind:'payment-confirmed',platformOrder,transactionId:platform==='TikTok'?platformOrder:'txn',isSandbox:true,platformCurrency:'CNY',tokenAmount:100};
  delete evidence.secret;
  if(platform==='TikTok'){
    const association={...evidence,kind:'prepayment-associated',requestHash:store.db.prepare('SELECT request_hash FROM erdon_tiktok_attempts').get().request_hash};
    await assert.rejects(recovery.apply(sign(association)),{code:'RECOVERY.WORKER_STOP_REQUIRED'});
    assert.equal((await recovery.apply(sign(association),{previousWorkerStopped:true})).associated,true);
    assert.equal(store.db.prepare('SELECT state FROM erdon_tiktok_attempts').get().state,'Pending');
    await recovery.apply(sign(association),{dryRun:false,previousWorkerStopped:true});
    assert.equal((await store.get(order.orderId)).state,'Created');
    evidence.evidenceId='paid-evidence';
  }
  for(const patch of [{subject:'bob'},{amountMinor:1},{appId:'other'},{platformOrder:'wrong'}])
    await assert.rejects(recovery.apply(sign({...evidence,...patch})),{code:'RECOVERY.EVIDENCE_MISMATCH'});
  const envelope=sign(evidence);assert.equal((await recovery.apply(envelope)).dryRun,true);
  assert.equal((await store.get(order.orderId)).state,'Created');
  assert.equal((await recovery.apply(envelope,{dryRun:false})).state,'Paid');
  assert.equal((await recovery.apply(envelope,{dryRun:false})).duplicate,true);
  const view=await recovery.inspect(order.orderId);assert.equal(view.audits.at(-1).reviewer,'operator-A');
  await assert.rejects(recovery.apply(sign({...evidence,transactionId:'other-transaction'}),{dryRun:false}),{code:'RECOVERY.EVIDENCE_REUSED'});
  await orders.fulfill(order.orderId,async()=>true);
  assert.equal((await recovery.apply(envelope,{dryRun:false})).state,'Fulfilled');
});
test('operator attestations reject tampering, wrong key, expiry and wrong metadata',()=>{
  const value={...metadata,kind:'authorization-revoked',platform:'TikTok',appId:'app',subject:'alice',environment:'test',gameId:'game',jobId:randomUUID()};
  assert.equal(verify(sign(value)).reviewer,'operator-A');
  const tampered=sign(value);tampered.payload+=' ';
  assert.throws(()=>verify(tampered),{code:'RECOVERY.EVIDENCE_INVALID'});
  assert.throws(()=>verify(signOperatorEvidence(value,'operator',Buffer.alloc(32,8))),{code:'RECOVERY.EVIDENCE_INVALID'});
  for(const patch of [{expiresAt:now},{issuedAt:now+600000},{sourceSha256:'fake'},{reviewer:''},{kind:'unknown'}])
    assert.throws(()=>verify(sign({...value,...patch})),{code:'RECOVERY.EVIDENCE_INVALID'});
});

test('CLI signs reviewed data, defaults to preview and persists one audit across reopen',async t=>{
  const dir=await mkdtemp(join(tmpdir(),'erdon-recovery-'));
  const db=join(dir,'orders.db'),configPath=join(dir,'config.json'),input=join(dir,'review.json'),signed=join(dir,'signed.json');
  const binding={platform:'Douyin',appId:'app',gameId:'game',environment:'test',secret:'secret'};
  let store=new SqliteOrderStore(db);t.after(async()=>{store.close();await rm(dir,{recursive:true,force:true});});
  const auth=new AuthService({bindings:[binding],store:new MemoryAuthStore(),verifiers:{Douyin:async()=>({subject:'alice'})}});
  const session=(await auth.exchange({...binding,code:'code'})).session,catalog=[{productId:'sword',productName:'Sword',amountMinor:600,currency:'CNY'}];
  const orders=new OrderService({binding,store,auth,catalog,verifyPayment:async()=>({paid:false})});
  const order=await orders.create(session,{productId:'sword',requestId:'first'});await new DouyinPayments({orders,catalog}).prepare(session,order.orderId);
  new PaymentRecovery({binding,store,verifyEvidence:verify});
  store.db.exec("CREATE TRIGGER reject_audit BEFORE INSERT ON erdon_payment_recovery BEGIN SELECT RAISE(ABORT,'audit unavailable'); END;");
  const time=Date.now(),payload={...metadata,...binding,kind:'payment-confirmed',issuedAt:time,expiresAt:time+60000,subject:'alice',orderId:order.orderId,
    productId:'sword',amountMinor:600,currency:'CNY',platformOrder:order.orderId,platformCurrency:'CNY',transactionId:'txn'};delete payload.secret;
  await writeFile(input,JSON.stringify(payload));
  await writeFile(configPath,JSON.stringify({schemaVersion:1,platform:'Douyin',gameId:'game',environment:'test',appIdEnv:'APP',secretEnv:'SECRET',
    storage:{auth:'auth.db',orders:'orders.db'},payments:{enabled:true,route:'Android',zoneId:'1',callbackTokenEnv:'CALLBACK',catalog}}));
  const run=promisify(execFile),cli=new URL('../src/recovery-cli.mjs',import.meta.url),env={...process.env,APP:'app',SDK_OPERATOR_KEY_HEX:key.toString('hex'),SDK_OPERATOR_KEY_ID:'operator'};
  const {fileURLToPath}=await import('node:url');
  const invoke=args=>run(process.execPath,[fileURLToPath(cli),...args],{env});
  const result=await invoke(['sign',input]);await writeFile(signed,result.stdout);
  assert.equal(createOperatorEvidenceVerifier({keys:new Map([['operator',key]])})(JSON.parse(await readFile(signed,'utf8'))).orderId,order.orderId);
  assert.equal(JSON.parse((await invoke(['payment',configPath,signed])).stdout).dryRun,true);
  await assert.rejects(invoke(['payment',configPath,signed,'--apply']),error=>JSON.parse(error.stderr).errorCode==='SERVER.STORAGE_UNAVAILABLE');
  assert.equal((await store.get(order.orderId)).state,'Created','failed audit rolls back Paid');
  store.db.exec('DROP TRIGGER reject_audit');
  await invoke(['payment',configPath,signed,'--apply']);store.close();store=new SqliteOrderStore(db);
  assert.equal((await store.get(order.orderId)).state,'Paid');
  assert.equal(JSON.parse((await invoke(['payment',configPath,signed,'--apply'])).stdout).duplicate,true);
  assert.equal(store.db.prepare('SELECT COUNT(*) AS n FROM erdon_payment_recovery').get().n,1);
});
