import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { Worker } from 'node:worker_threads';
import { DatabaseSync } from 'node:sqlite';
import { SqliteAuthStore } from '../src/sqlite-store.mjs';
import { AuthService, digest } from '../src/auth.mjs';

async function database(t) {
  const directory = await mkdtemp(join(tmpdir(), 'erdon-auth-test-'));
  t.closeStores = [];
  t.after(async () => {
    for (const close of t.closeStores) close();
    await rm(directory, { recursive: true, force: true });
  });
  return join(directory, 'auth.sqlite');
}
test('durable sessions survive reopening and retain auth isolation/revocation', async t => {
  const path = await database(t);
  const binding = { gameId:'game', appId:'app', platform:'WeChat', environment:'test', secret:'secret' };
  const create = store => new AuthService({ store, bindings:[binding], verifiers:{WeChat:async()=>({subject:'id'})} });
  let store = new SqliteAuthStore(path); t.closeStores.push(()=>store.close());
  const { session } = await create(store).exchange({ ...binding, code:'private-code' });
  store.close(); store = new SqliteAuthStore(path);
  const service = create(store);
  assert.equal((await service.authenticate(session,binding)).subject,'id');
  await assert.rejects(service.authenticate(session,{...binding,gameId:'other'}),{code:'AUTH.SESSION_INVALID'});
  await assert.rejects(service.exchange({...binding,code:'private-code'}),{code:'AUTH.CODE_REUSED'});
  const inspection = new DatabaseSync(path);
  assert.equal(inspection.prepare('SELECT hash FROM erdon_auth_sessions').get().hash,digest(session)); inspection.close();
  await service.revoke(session); store.close(); store = new SqliteAuthStore(path);
  await assert.rejects(create(store).authenticate(session,binding),{code:'AUTH.SESSION_INVALID'});
});
test('expiration frees capacity; storage failure is safe', async t => {
  const path = await database(t); let clock=1000;
  const store = new SqliteAuthStore(path,{now:()=>clock,capacity:1}); t.closeStores.push(()=>store.close());
  assert.equal(await store.claim('a',1100),true);
  assert.equal(await store.claim('a',1200),false);
  await assert.rejects(store.claim('b',1200),{code:'SERVER.CAPACITY'});
  await store.putSession('s',{binding:'b',subject:'id',expiresAt:1100});
  clock=1100;
  assert.equal(await store.getSession('s'),null);
  assert.equal(await store.claim('a',1200),true);
  store.close(); await assert.rejects(store.getSession('s'),{code:'SERVER.STORAGE_UNAVAILABLE'});
});
test('independent database connections admit only one concurrent claim', async t => {
  const path=await database(t); new SqliteAuthStore(path).close();
  const moduleUrl=new URL('../src/sqlite-store.mjs',import.meta.url).href;
  const run=()=>new Promise((resolve,reject)=>{
    const worker=new Worker(`
      const { parentPort, workerData } = require('node:worker_threads');
      import(workerData.moduleUrl).then(async ({ SqliteAuthStore }) => {
        const store = new SqliteAuthStore(workerData.path);
        try { parentPort.postMessage(await store.claim('same-code-hash', Date.now()+60000)); }
        finally { store.close(); }
      }).catch(error => { throw error; });
    `,{eval:true,workerData:{path,moduleUrl}});
    let result;
    worker.on('message',value=>{result=value;}); worker.on('error',reject);
    worker.on('exit',code=>code===0 ? resolve(result) : reject(new Error('worker failed')));
  });
  const results=await Promise.all([run(),run(),run(),run()]);
  assert.equal(results.filter(value=>value===true).length,1);
  assert.equal(results.filter(value=>value===false).length,3);
});
test('unknown schema is refused without migrating user data', async t => {
  const path=await database(t); const db=new DatabaseSync(path);
  db.exec('CREATE TABLE erdon_auth_schema(version INTEGER); INSERT INTO erdon_auth_schema VALUES (999)'); db.close();
  assert.throws(()=>new SqliteAuthStore(path),/Cannot initialize/);
  const check=new DatabaseSync(path); assert.equal(check.prepare('SELECT version FROM erdon_auth_schema').get().version,999); check.close();
});
