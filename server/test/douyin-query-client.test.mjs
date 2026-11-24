import test from 'node:test';
import assert from 'node:assert/strict';
import { createDouyinQueryClient } from '../src/douyin-query-client.mjs';

const binding = { platform: 'Douyin', appId: 'test-app', secret: 'private-secret' };
const orderId = 'c501a92e-e11a-4d6a-857f-388295c6e926';
const tokenResult = () => Response.json({ err_no: 0, data: { access_token: 'private-token', expires_in: 7200 } });

test('Douyin query uses game token endpoint, coalesces refresh and encodes only server order ID', async () => {
  let time = 1000, tokens = 0, queries = 0;
  const client = createDouyinQueryClient({ binding, now: () => time, fetchImpl: async (input, options) => {
    const url = new URL(input);
    assert.equal(options.redirect, 'error'); assert.ok(options.signal instanceof AbortSignal);
    if (url.pathname.endsWith('/token')) {
      tokens++; assert.equal(url.origin, 'https://minigame.zijieapi.com');
      assert.equal(options.method, 'POST');
      assert.deepEqual(JSON.parse(options.body), { appid: binding.appId, secret: binding.secret, grant_type: 'client_credential' });
      return tokenResult();
    }
    queries++; assert.equal(url.origin, 'https://developer.toutiao.com');
    assert.equal(url.pathname, '/api/apps/game/payment/queryPayState'); assert.equal(options.method, 'GET');
    assert.deepEqual([...url.searchParams], [['access_token', 'private-token'], ['orderno', orderId]]);
    return Response.json({ status: 'success' });
  } });
  assert.deepEqual(await Promise.all([client.query(orderId), client.query(orderId)]), [{ status: 'success' }, { status: 'success' }]);
  assert.equal(tokens, 1); assert.equal(queries, 2);
  time += 3599000; await client.query(orderId); assert.equal(tokens, 1);
  time += 1000; await client.query(orderId); assert.equal(tokens, 2);
});

test('Douyin credentials errors remain bounded, safe and retryable on next request', async () => {
  const bad = [Response.json({ err_no: 40017, err_tips: 'private-secret' }), Response.json({ err_no: 0, data: { access_token: 'x', expires_in: 0 } }),
    new Response('private-secret', { status: 503 }), new Response('x'.repeat(16385)), new Response(new Uint8Array([0xff]))];
  for (const response of bad) {
    let attempt = 0;
    const client = createDouyinQueryClient({ binding, fetchImpl: async input => {
      if (new URL(input).pathname.endsWith('/token')) return attempt++ === 0 ? response : tokenResult();
      return Response.json({ status: 'unsuccess' });
    } });
    await assert.rejects(client.query(orderId), error => error.code.startsWith('PAY.') && !error.message.includes('private'));
    assert.deepEqual(await client.query(orderId), { status: 'unsuccess' });
  }
});

test('Douyin query rejects unknown/contradictory status, oversized, malformed and failed responses', async () => {
  const bad = [Response.json({ status: 'SUCCESS' }), Response.json({ status: 'success', err_no: 1 }),
    Response.json({ status: 'success', error: 'failure' }), Response.json({ data: { status: 'success' } }),
    new Response('[]'), new Response('null'), new Response(new Uint8Array([0xff])), new Response('x'.repeat(16385)),
    new Response('private-token', { status: 302 })];
  for (const response of bad) {
    const client = createDouyinQueryClient({ binding, fetchImpl: async input => new URL(input).pathname.endsWith('/token') ? tokenResult() : response });
    await assert.rejects(client.query(orderId), error => ['PAY.QUERY_RESPONSE_INVALID', 'PAY.QUERY_UNAVAILABLE'].includes(error.code) && !error.message.includes('private'));
  }
});

test('invalid query input never reaches network; timeout errors do not disclose token URL', async () => {
  let calls = 0;
  const client = createDouyinQueryClient({ binding, fetchImpl: async input => {
    calls++; if (new URL(input).pathname.endsWith('/token')) return tokenResult();
    throw new Error(String(input));
  } });
  await assert.rejects(client.query('forged&access_token=secret'), TypeError); assert.equal(calls, 0);
  await assert.rejects(client.query(orderId), { code: 'PAY.QUERY_UNAVAILABLE', message: 'PAY.QUERY_UNAVAILABLE' });
  for (const timeoutMs of [0, -1, Infinity, 120001]) assert.throws(() => createDouyinQueryClient({ binding, timeoutMs }), TypeError);
});
