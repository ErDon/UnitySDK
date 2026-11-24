import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { createHash } from 'node:crypto';
import { createWeChatApplication } from '../src/wechat-application.mjs';
import { wechatHmac } from '../src/wechat-payments.mjs';

test('WeChat HTTP login/preparation/query/callback delivers once and never exposes keys', async t => {
  const logs = []; let tokenCalls = 0, delivered = 0, allowDelivery = false;
  const binding = { platform: 'WeChat', gameId: 'game', appId: 'wxapp', environment: 'test', secret: 'private-app-secret' };
  const app = createWeChatApplication({ binding, catalog: [{ productId: 'sword', amountMinor: 600, currency: 'CNY' }],
    callbackToken: 'callback-token', authPath: ':memory:', orderPath: ':memory:', credentialPath: ':memory:',
    keys: new Map([['v1', Buffer.alloc(32, 19)]]), currentKeyId: 'v1', offerId: 'offer', zoneId: '1',
    appKey: 'private-app-key', isSandbox: true, originalId: 'gh_game', log: line => logs.push(line),
    deliver: async () => { if (!allowDelivery) throw new Error('private-delivery-error'); delivered++; return true; },
    deliveryIntervalMs: 60000,
    fetchImpl: async (url, options) => {
      const parsed = new URL(url);
      if (parsed.pathname === '/sns/jscode2session') return Response.json({ openid: 'alice', session_key: 'private-session-key' });
      if (parsed.pathname === '/cgi-bin/stable_token') { tokenCalls++; assert.equal(JSON.parse(options.body).force_refresh, false); return Response.json({ access_token: 'private-access', expires_in: 7200 }); }
      assert.equal(parsed.pathname, '/wxa/game/queryorderinfo');
      assert.equal(parsed.searchParams.get('signature'), wechatHmac('private-session-key', options.body));
      assert.equal(parsed.searchParams.get('pay_sig'), wechatHmac('private-app-key', parsed.pathname + '&' + options.body));
      const body = JSON.parse(options.body);
      return Response.json({ errcode: 0, product_id: 'sword', out_trade_no: body.out_trade_no, pay_state: 2, deliver_state: 1 });
    } });
  const server = createServer(app.handler);
  t.after(async () => { await new Promise(resolve => server.close(resolve)); await app.close(); });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const base = `http://127.0.0.1:${server.address().port}`;
  async function post(path, data, session) {
    const response = await fetch(base + path, { method: 'POST', headers: { 'Content-Type': 'application/json', ...(session ? { Authorization: 'Bearer ' + session } : {}) }, body: JSON.stringify(data) });
    return { status: response.status, data: await response.json() };
  }
  const login = await post('/v1/session', { ...binding, code: 'code' });
  assert.equal(login.status, 200); const session = login.data.session;
  const created = await post('/v1/orders', { productId: 'sword', requestId: 'first' }, session);
  assert.equal(created.status, 200); const id = created.data.order.orderId;
  const prepared = await post(`/v1/orders/${id}/prepare`, {}, session);
  assert.equal(prepared.status, 200); const ready = prepared.data.prepayment, sign = JSON.parse(ready.signData);
  for (let i = 0; i < 2; i++) {
    const query = await post(`/v1/orders/${id}/query`, {}, session);
    assert.equal(query.status, 200); assert.equal(query.data.query.state, 'Created');
  }
  assert.equal(tokenCalls, 1);
  const fields = { timestamp: '100', nonce: 'nonce' };
  fields.signature = createHash('sha1').update(['callback-token', fields.timestamp, fields.nonce].sort().join('')).digest('hex');
  const path = '/v1/webhooks/wechat/payment?' + new URLSearchParams(fields);
  const challenge = await fetch(base + path + '&echostr=hello'); assert.equal(await challenge.text(), 'hello');
  const payload = JSON.stringify({ OpenId: 'alice', Env: 1, OutTradeNo: sign.outTradeNo,
    GoodsInfo: { ProductId: 'sword', Quantity: 1, ZoneId: '1', OrigPrice: 600, ActualPrice: 600, Attach: sign.attach, OrderSource: 1 },
    WeChatPayInfo: { TransactionId: 'transaction' } });
  const event = 'minigame_game_pay_goods_deliver_notify';
  const envelope = { ToUserName: 'gh_game', MsgType: 'event', Event: event, CreateTime: 100,
    MiniGame: { Payload: payload, PayEventSig: wechatHmac('private-app-key', event + '&' + payload) } };
  const failed = await post(path, envelope); assert.equal(failed.status, 502); assert.equal(failed.data.ErrCode, 1);
  assert.equal((await app.orders.store.get(id)).state, 'Paid');
  allowDelivery = true;
  for (let i = 0; i < 2; i++) assert.deepEqual((await post(path, envelope)).data, { ErrCode: 0, ErrMsg: 'Success' });
  assert.equal(delivered, 1); assert.equal((await app.orders.store.get(id)).state, 'Fulfilled');
  assert.equal((await post(path + '&encrypt_type=aes', envelope)).status, 400);
  const output = JSON.stringify([login, prepared, logs]);
  for (const secret of ['private-app-secret', 'private-session-key', 'private-app-key', 'private-access', 'private-delivery-error']) assert.equal(output.includes(secret), false);
});
