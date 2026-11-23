import test from 'node:test';
import assert from 'node:assert/strict';
import { createTikTokTradeClient } from '../src/tiktok-trade-client.mjs';
const input = { accessToken: 'private-token', tokenAmount: 100, orderId: 'order', productId: 'coins',
  productName: '金币', orderUrl: '/orders/order', quantity: 1, quantityUnit: 'item', imageUrl: 'https://example.com/coins.png' };
const ok = { data: { trade_order_id: 'platform-trade' }, error: { code: 'ok', message: '', log_id: 'not-forwarded' } };
test('prepayment uses fixed authenticated endpoint and only documented server fields', async () => {
  let calls = 0;
  const client = createTikTokTradeClient({ fetchImpl: async (url, options) => {
    calls++; assert.equal(url, 'https://open.tiktokapis.com/v2/minis/trade_order/create/');
    assert.equal(options.method, 'POST'); assert.equal(options.redirect, 'error'); assert.ok(options.signal);
    assert.equal(options.headers.Authorization, 'Bearer private-token');
    assert.deepEqual(JSON.parse(options.body), { token_type: 'BEANS', token_amount: 100, order_info: {
      order_id: 'order', product_id: 'coins', product_name: '金币', order_url: '/orders/order',
      quantity: 1, quantity_unit: 'item', image_url: 'https://example.com/coins.png' } });
    return Response.json(ok);
  } });
  const result = await client.create({ ...input, clientPrice: 1 });
  assert.deepEqual(result, { tradeOrderId: 'platform-trade' }); assert.equal(Object.isFrozen(result), true); assert.equal(calls, 1);
});
test('invalid local values are rejected before sending any request', async () => {
  let calls = 0; const client = createTikTokTradeClient({ fetchImpl: async () => { calls++; } });
  for (const patch of [{ tokenAmount: 0 }, { tokenAmount: 1.5 }, { accessToken: 'a\nb' }, { quantity: -1 },
    { productId: '' }, { imageUrl: 'http://example.com/a' }, { imageUrl: 'https://user:password@example.com/a' }])
    await assert.rejects(client.create({ ...input, ...patch }), TypeError);
  assert.equal(calls, 0);
});
test('ambiguous responses and transport failures never retry or leak upstream text', async () => {
  for (const body of [null, [], {}, { ...ok, error: { code: 'failed', message: 'private-secret' } },
    { ...ok, data: { trade_order_id: '' } }]) {
    let calls = 0; const client = createTikTokTradeClient({ fetchImpl: async () => { calls++; return Response.json(body); } });
    await assert.rejects(client.create(input), { code: 'PAY.PREPAYMENT_UNCERTAIN', message: 'PAY.PREPAYMENT_UNCERTAIN' });
    assert.equal(calls, 1);
  }
  for (const fetchImpl of [async () => { throw new Error('private-token'); },
    async () => new Response('private-secret', { status: 503 }), async () => new Response('x'.repeat(16385))])
    await assert.rejects(createTikTokTradeClient({ fetchImpl }).create(input), { code: 'PAY.PREPAYMENT_UNCERTAIN' });
  await assert.rejects(createTikTokTradeClient({ fetchImpl: async () => new Response('private', { status: 401 }) }).create(input),
    { code: 'PAY.PLATFORM_TOKEN_REJECTED' });
});
