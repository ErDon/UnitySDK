import test from 'node:test';
import assert from 'node:assert/strict';
import { createTikTokTradeClient } from '../src/tiktok-trade-client.mjs';

const input={accessToken:'private-token',tradeOrderId:'trade-1'};
const envelope=status=>({data:{trade_order_id:input.tradeOrderId,trade_order_status:status},error:{code:'ok'}});
test('query uses fixed authenticated endpoint and returns only matching known status',async()=>{
  for(const status of ['PENDING','SUCCESS']) {
    const client=createTikTokTradeClient({fetchImpl:async(url,options)=>{
      assert.equal(url,'https://open.tiktokapis.com/v2/minis/trade_order/query/');
      assert.equal(options.method,'POST');assert.equal(options.redirect,'error');assert.ok(options.signal);
      assert.equal(options.headers.Authorization,'Bearer private-token');
      assert.deepEqual(JSON.parse(options.body),{trade_order_id:'trade-1'});
      return Response.json(envelope(status));
    }});
    const result=await client.query(input);
    assert.deepEqual(result,{tradeOrderId:'trade-1',status});assert.ok(Object.isFrozen(result));
    assert.equal(result.paid,undefined);
  }
});
test('query rejects mismatched, unknown, malformed, oversized and invalid UTF-8 responses',async()=>{
  for(const body of [null,[],{}, {...envelope('SUCCESS'),error:{code:'private-error'}},
    {data:{trade_order_id:'other',trade_order_status:'SUCCESS'},error:{code:'ok'}},envelope('REFUNDED'),envelope(null)]) {
    await assert.rejects(createTikTokTradeClient({fetchImpl:async()=>Response.json(body)}).query(input),
      {code:'PAY.QUERY_RESPONSE_INVALID',message:'PAY.QUERY_RESPONSE_INVALID'});
  }
  for(const body of ['x'.repeat(16385),new Uint8Array([0xc3,0x28])])
    await assert.rejects(createTikTokTradeClient({fetchImpl:async()=>new Response(body)}).query(input),{code:'PAY.QUERY_RESPONSE_INVALID'});
});
test('query errors are safe and invalid arguments never send a request',async()=>{
  for(const status of [401,429,500]) {
    let count=0;
    await assert.rejects(createTikTokTradeClient({fetchImpl:async()=>{count++;return new Response('private-server-message',{status});}}).query(input),
      {code:status===401?'PAY.PLATFORM_TOKEN_REJECTED':'PAY.QUERY_UNAVAILABLE'});
    assert.equal(count,1);
  }
  await assert.rejects(createTikTokTradeClient({fetchImpl:async()=>{throw new Error('private-token');}}).query(input),
    {code:'PAY.QUERY_UNAVAILABLE',message:'PAY.QUERY_UNAVAILABLE'});
  let count=0;
  const client=createTikTokTradeClient({fetchImpl:async()=>{count++;}});
  for(const invalid of [{...input,accessToken:'bad\nheader'},{...input,tradeOrderId:' '},{...input,tradeOrderId:'x'.repeat(257)}])
    await assert.rejects(client.query(invalid),TypeError);
  assert.equal(count,0);
});
