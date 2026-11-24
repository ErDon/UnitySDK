import test from 'node:test';
import assert from 'node:assert/strict';
import { AuthService, MemoryAuthStore } from '../src/auth.mjs';
import { SqliteOrderStore } from '../src/sqlite-orders.mjs';
import { OrderService } from '../src/orders.mjs';
import { WeChatCredentialStore } from '../src/wechat-credential-store.mjs';
import { WeChatPayments, wechatHmac } from '../src/wechat-payments.mjs';
import { createWeChatVerifier } from '../src/wechat.mjs';

const binding = { platform: 'WeChat', gameId: 'game', appId: 'wxapp', environment: 'test', secret: 'secret' };
const key = Buffer.alloc(32, 17);
async function fixture(t) {
  const vault = new WeChatCredentialStore(':memory:', { keys: new Map([['v1', key]]), currentKeyId: 'v1' });
  const store = new SqliteOrderStore(':memory:'); t.after(() => { vault.close(); store.close(); });
  const auth = new AuthService({ bindings: [binding], store: new MemoryAuthStore(), verifiers: { WeChat: async (_, code) => ({ subject: code }) } });
  const session = (await auth.exchange({ ...binding, code: 'alice' })).session;
  const other = (await auth.exchange({ ...binding, code: 'bob' })).session;
  await vault.put(binding, 'alice', 'session-key');
  const orders = new OrderService({ binding, auth, store, catalog: [{ productId: 'sword', amountMinor: 600, currency: 'CNY' }], verifyPayment: async () => ({ paid: false }) });
  const payments = new WeChatPayments({ orders, credentialStore: vault, offerId: 'offer', zoneId: '1', appKey: 'pay-key', isSandbox: true, originalId: 'gh_game' });
  const order = await orders.create(session, { productId: 'sword', requestId: 'first' });
  return { vault, store, auth, session, other, orders, payments, order };
}
function notification(ready, change = () => {}) {
  const data = JSON.parse(ready.signData);
  const payload = { OpenId: 'alice', Env: data.env, OutTradeNo: data.outTradeNo,
    GoodsInfo: { ProductId: data.productId, Quantity: 1, ZoneId: data.zoneId, OrigPrice: 600, ActualPrice: 600, Attach: data.attach, OrderSource: 1 },
    WeChatPayInfo: { TransactionId: 'transaction' } };
  change(payload);
  const event = 'minigame_game_pay_goods_deliver_notify', raw = JSON.stringify(payload);
  return { ToUserName: 'gh_game', MsgType: 'event', Event: event, CreateTime: 100, MiniGame: { Payload: raw, PayEventSig: wechatHmac('pay-key', event + '&' + raw) } };
}
test('official HMAC test vectors match exact UTF-8 input', () => {
  const body = '{"offer_id": "12345678", "openid": "oUrsfxxxxxxxxxx", "ts": 1668136271, "zone_id": "1", "env": 0}';
  assert.equal(wechatHmac('12345', '/wxa/game/getbalance&' + body), '11bac6388871d29c055c7d16fbe42e8d646855b666faf89b15c815218b1b23bd');
  assert.equal(wechatHmac('9hAb/NEYUlkaMBEsmFgzig==', body), '42fe1d3341fb1c8bd6f5014ba735ab04eacc80a2deb3ab4669eab4700b5b6729');
});
test('prepare is owned, stable, 32-char mapped and refreshes only user signature after login', async t => {
  const f = await fixture(t);
  await assert.rejects(f.payments.prepare(f.other, f.order.orderId), { code: 'PAY.ORDER_NOT_FOUND' });
  const ready = await f.payments.prepare(f.session, f.order.orderId);
  assert.match(JSON.parse(ready.signData).outTradeNo, /^[a-f0-9]{32}$/);
  assert.deepEqual(await f.payments.prepare(f.session, f.order.orderId), ready);
  await f.vault.put(binding, 'alice', 'new-key');
  const next = await f.payments.prepare(f.session, f.order.orderId);
  assert.equal(next.signData, ready.signData); assert.equal(next.paySignature, ready.paySignature);
  assert.notEqual(next.sessionSignature, ready.sessionSignature);
  assert.equal(JSON.stringify(next).includes('new-key'), false);
});
test('verified notification is idempotent, client confirm cannot pay, delivery is separate', async t => {
  const f = await fixture(t), ready = await f.payments.prepare(f.session, f.order.orderId);
  await assert.rejects(f.orders.confirm(f.session, f.order.orderId, { paid: true }), { code: 'PAY.NOT_PAID' });
  assert.equal((await f.payments.acceptWebhook(notification(ready))).state, 'Paid');
  assert.equal((await f.payments.acceptWebhook(notification(ready))).state, 'Paid');
  let count = 0;
  await f.orders.fulfill(f.order.orderId, async () => { count++; return true; });
  assert.equal((await f.payments.acceptWebhook(notification(ready))).state, 'Fulfilled');
  await f.orders.fulfill(f.order.orderId, async () => { count++; return true; });
  assert.equal(count, 1);
});
test('signed mismatches, tampering, mock and transaction reuse never incorrectly grant', async t => {
  const f = await fixture(t), ready = await f.payments.prepare(f.session, f.order.orderId);
  for (const mutate of [x => x.OpenId = 'bob', x => x.Env = 0, x => x.GoodsInfo.ActualPrice = 1,
    x => x.GoodsInfo.ProductId = 'gold', x => x.GoodsInfo.Attach = 'fake', x => x.GoodsInfo.Quantity = 2])
    await assert.rejects(f.payments.acceptWebhook(notification(ready, mutate)), { code: 'PAY.VERIFICATION_MISMATCH' });
  const bad = notification(ready); bad.MiniGame.Payload += ' ';
  await assert.rejects(f.payments.acceptWebhook(bad), { code: 'WEBHOOK.INVALID_SIGNATURE' });
  const mock = notification(ready); mock.MiniGame.IsMock = true;
  assert.equal((await f.payments.acceptWebhook(mock)).mock, true);
  assert.equal((await f.orders.get(f.session, f.order.orderId)).state, 'Created');
  await f.payments.acceptWebhook(notification(ready));
  const second = await f.orders.create(f.session, { productId: 'sword', requestId: 'second' });
  await assert.rejects(f.payments.acceptWebhook(notification(await f.payments.prepare(f.session, second.orderId))), { code: 'PAY.TRANSACTION_REUSED' });
});
test('vault authenticates scope and ciphertext, expires locally, and login never returns session_key', async t => {
  let now = 100;
  const vault = new WeChatCredentialStore(':memory:', { keys: new Map([['v1', key]]), currentKeyId: 'v1', now: () => now, maxAgeMs: 100 });
  t.after(() => vault.close());
  const verify = createWeChatVerifier({ credentialStore: vault, fetchImpl: async () => Response.json({ openid: 'alice', session_key: 'super-secret-key' }) });
  assert.deepEqual(await verify(binding, 'code'), { subject: 'alice' });
  assert.equal((await vault.get(binding, 'alice')).sessionKey, 'super-secret-key');
  const raw = vault.db.prepare('SELECT * FROM erdon_wechat_credentials').get();
  assert.equal(Buffer.from(raw.ciphertext).includes(Buffer.from('super-secret-key')), false);
  assert.equal(await vault.get({ ...binding, gameId: 'other' }, 'alice'), null);
  vault.db.prepare('UPDATE erdon_wechat_credentials SET tag=?').run(Buffer.alloc(16));
  await assert.rejects(vault.get(binding, 'alice'), { code: 'PAY.CREDENTIAL_UNAVAILABLE' });
  now = 200; assert.equal(await vault.get(binding, 'alice'), null);
});
