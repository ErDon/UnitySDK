import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { createHmac } from 'node:crypto';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createTikTokApplication } from '../src/tiktok-application.mjs';

test('application close drains configured delivery worker before closing databases',async t=>{
  let entered,release;const started=new Promise(resolve=>entered=resolve);
  const binding={gameId:'game',appId:'app',environment:'test',platform:'TikTok',secret:'secret'};
  const app=createTikTokApplication({binding,isSandbox:true,authPath:':memory:',orderPath:':memory:',credentialPath:':memory:',
    keys:new Map([['key',Buffer.alloc(32,8)]]),currentKeyId:'key',log:()=>{},deliveryIntervalMs:100,
    catalog:[{productId:'coins',amountMinor:99,currency:'USD',tokenAmount:100,productName:'Coins',quantity:1,quantityUnit:'item',imageUrl:'https://example.com/item.png'}],
    fetchImpl:async()=>Response.json({open_id:'player',access_token:'private-token',token_type:'Bearer',expires_in:3600}),
    deliver:async()=>{entered();await new Promise(resolve=>release=resolve);return true;}
  });
  t.after(async()=>{release?.();await app.close();});
  const session=(await app.orders.auth.exchange({...binding,code:'code'})).session;
  const order=await app.orders.create(session,{productId:'coins',requestId:'one'});
  await app.orders.store.markPaid(order.orderId,'trusted-test-transaction');
  const deadline=setTimeout(()=>entered(),2000);
  try {
    await started;assert.equal(typeof release,'function');
    let closed=false;const closing=app.close().then(()=>closed=true);
    await Promise.resolve();assert.equal(closed,false);assert.equal(app.orders.store.closed,false);
    release();await closing;assert.equal(app.orders.store.closed,true);
    await app.close();
  }finally{clearTimeout(deadline);}
});

test('composed application authenticates, prepares, accepts signed payment and resumes after restart', async t => {
  const dir = await mkdtemp(join(tmpdir(), 'erdon-app-'));
  let app, server, base, creates = 0;
  const logs = [], timestamp = 1800000000;
  let clock = timestamp * 1000, refreshes = 0;
  const binding = { gameId:'game', appId:'app', environment:'test', platform:'TikTok', secret:'private-secret' };
  const config = { binding, isSandbox:true, now:()=>clock, log:line=>logs.push(line),
    authPath:join(dir,'auth.db'), orderPath:join(dir,'orders.db'), credentialPath:join(dir,'credentials.db'),
    keys:new Map([['key',Buffer.alloc(32,9)]]), currentKeyId:'key',
    catalog:[{ productId:'coins', amountMinor:99, currency:'USD', tokenAmount:100, productName:'Coins',
      quantity:1, quantityUnit:'item', imageUrl:'https://example.com/coins.png' }],
    fetchImpl:async(url, options)=>{
      if (url.endsWith('/oauth/token/')) {
        const form = new URLSearchParams(options.body);
        if (form.get('grant_type') === 'refresh_token') {
          assert.equal(form.get('refresh_token'), 'private-refresh'); refreshes++;
          return Response.json({ open_id:'player', access_token:'private-renewed', token_type:'Bearer', expires_in:3600,
            refresh_token:'private-rotated', refresh_expires_in:7200 });
        }
        return Response.json({ open_id:'player', access_token:'private-token', token_type:'Bearer', expires_in:120,
          refresh_token:'private-refresh', refresh_expires_in:7200 });
      }
      if (url.endsWith('/trade_order/query/')) {
        assert.equal(options.headers.Authorization,'Bearer private-renewed');
        assert.deepEqual(JSON.parse(options.body),{trade_order_id:'trade'});
        return Response.json({data:{trade_order_id:'trade',trade_order_status:'SUCCESS'},error:{code:'ok'}});
      }
      assert.ok(url.endsWith('/trade_order/create/'));
      assert.equal(options.headers.Authorization,'Bearer private-renewed'); creates++;
      return Response.json({ data:{trade_order_id:'trade'}, error:{code:'ok'} });
    }
  };
  const stop = async()=>{
    if (server) { await new Promise(resolve=>{server.close(resolve);server.closeAllConnections();}); server=null; }
    app?.close();
  };
  t.after(async()=>{await stop();await rm(dir,{recursive:true,force:true});});
  const start=async()=>{
    app=createTikTokApplication(config);server=createServer(app.handler);
    await new Promise((resolve,reject)=>{server.once('error',reject);server.listen(0,'127.0.0.1',resolve);});
    base=`http://127.0.0.1:${server.address().port}`;
  };
  const request=async(path, body, session)=>{
    const res=await fetch(base+path,{method:body===undefined?'GET':'POST',headers:{'Content-Type':'application/json',
      ...(session?{Authorization:'Bearer '+session}:{})},body:body===undefined?undefined:JSON.stringify(body)});
    return {status:res.status,data:await res.json()};
  };
  await start();
  const login=await request('/v1/session',{...binding,secret:undefined,code:'login'});
  assert.equal(login.status,200);const session=login.data.session;
  assert.ok(!JSON.stringify(login).includes('private-'));
  clock += 61000; // Payment preparation refreshes a token within its final minute.
  const created=await request('/v1/orders',{productId:'coins',requestId:'purchase'},session);
  assert.equal(created.status,200);const id=created.data.order.orderId;
  const prepared=await request(`/v1/orders/${id}/prepare`,{accessToken:'forged',tokenAmount:1},session);
  assert.equal(prepared.status,200);assert.equal(prepared.data.prepayment.tradeOrderId,'trade');
  assert.equal((await request(`/v1/orders/${id}/confirm`,{evidence:'success'},session)).status,409);
  await stop();await start();
  assert.equal((await request(`/v1/orders/${id}/prepare`,{},session)).status,200);
  assert.equal(creates,1);
  assert.equal(refreshes,1); // Restart retains the rotated token without another refresh.
  const queried=await request(`/v1/orders/${id}/query`,{tradeOrderId:'forged',accessToken:'forged'},session);
  assert.equal(queried.status,200);
  assert.deepEqual(queried.data.query,{orderId:id,platformStatus:'SUCCESS',state:'Created'});
  assert.equal((await request(`/v1/orders/${id}/query`,{})).status,401);
  const raw=JSON.stringify({client_key:'app',event:'minis.trade_order.redeem.success',create_time:timestamp,user_openid:'player',
    content:JSON.stringify({order_id:id,trade_order_id:'trade',is_sandbox:true})});
  const signature=`t=${timestamp},s=${createHmac('sha256',binding.secret).update(`${timestamp}.`).update(raw).digest('hex')}`;
  for(let i=0;i<2;i++) {
    const notified=await fetch(base+'/v1/webhooks/tiktok/payment',{method:'POST',headers:{'Content-Type':'application/json','TikTok-Signature':signature},body:raw});
    assert.equal(notified.status,200);await notified.json();
  }
  assert.equal((await app.pendingDelivery()).length,1);
  await app.orders.fulfill(id,async message=>{assert.equal(message.subject,'player');return true;});
  assert.equal((await request(`/v1/orders/${id}`,undefined,session)).data.order.state,'Fulfilled');
  assert.equal((await request('/v1/orders/'+id+'/fulfill',{},session)).status,404);
  assert.ok(!logs.join('').includes('private-'));
  app.close();app.close();
  assert.equal((await request('/v1/session',{})).status,503);
});
