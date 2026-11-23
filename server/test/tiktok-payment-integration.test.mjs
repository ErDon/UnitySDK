import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { createHmac } from 'node:crypto';
import { DatabaseSync } from 'node:sqlite';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { OrderService } from '../src/orders.mjs';
import { SqliteOrderStore } from '../src/sqlite-orders.mjs';
import { TikTokPrepayments } from '../src/tiktok-prepayments.mjs';
import { TikTokPrepaymentFlow } from '../src/tiktok-prepayment-flow.mjs';
import { createTikTokTradeClient } from '../src/tiktok-trade-client.mjs';
import { createTikTokPaymentWebhookHandler } from '../src/tiktok-webhook-http.mjs';

test('local payment chain grants once after HTTP duplicates, lost delivery acknowledgment and database restart', async t => {
  const dir = await mkdtemp(join(tmpdir(), 'erdon-tiktok-chain-'));
  const stores = [], receivers = [], servers = [];
  t.after(async () => {
    for (const server of servers) await new Promise(resolve => { server.close(resolve); server.closeAllConnections(); });
    for (const store of stores) store.close();
    for (const receiver of receivers) { try { receiver.close(); } catch { } }
    await rm(dir, { recursive: true, force: true });
  });
  const binding = { gameId: 'game', appId: 'app', environment: 'sandbox', platform: 'TikTok' };
  const clientSecret = 'offline-signing-key', timestamp = 1800000000;
  const open = () => {
    const store = new SqliteOrderStore(join(dir, 'orders.sqlite')); stores.push(store);
    const service = new OrderService({ binding, store, catalog: [{ productId: 'coins', amountMinor: 99, currency: 'USD' }],
      auth: { authenticate: async () => ({ subject: 'player' }) },
      verifyPayment: async () => { throw new Error('Client evidence must not confirm this payment'); } });
    return { store, service, payments: new TikTokPrepayments(store) };
  };
  // Test-only game entitlement receiver: deduplication and grant commit together.
  const openReceiver = () => {
    const db = new DatabaseSync(join(dir, 'game.sqlite')); receivers.push(db);
    db.exec(`CREATE TABLE IF NOT EXISTS deliveries(delivery_key TEXT PRIMARY KEY);
      CREATE TABLE IF NOT EXISTS wallet(subject TEXT PRIMARY KEY, coins INTEGER NOT NULL);`);
    return db;
  };
  const grant = (db, message) => {
    db.exec('BEGIN IMMEDIATE');
    try {
      const inserted = db.prepare('INSERT OR IGNORE INTO deliveries VALUES (?)').run(message.deliveryKey);
      if (inserted.changes) db.prepare(`INSERT INTO wallet VALUES (?,100)
        ON CONFLICT(subject) DO UPDATE SET coins=coins+100`).run(message.subject);
      db.exec('COMMIT'); return true;
    } catch (error) { db.exec('ROLLBACK'); throw error; }
  };
  let current = open(), receiver = openReceiver(), creates = 0;
  const order = await current.service.create('game-session', { productId: 'coins', requestId: 'purchase-1' });
  const transport = createTikTokTradeClient({ fetchImpl: async (_url, options) => {
    creates++; const request = JSON.parse(options.body);
    assert.equal(request.order_info.order_id, order.orderId); assert.equal(request.token_amount, 100);
    return Response.json({ data: { trade_order_id: 'platform-trade' }, error: { code: 'ok' } });
  } });
  const flow = new TikTokPrepaymentFlow(current.payments, transport);
  const input = { binding, isSandbox: true, openId: 'platform-player', accessToken: 'offline-token', orderId: order.orderId,
    tokenAmount: 100, productName: 'Coins', orderUrl: '/orders/' + order.orderId, quantity: 1, quantityUnit: 'item', imageUrl: 'https://example.com/coins.png' };
  assert.equal((await flow.create(input)).tradeOrderId, 'platform-trade');
  await flow.create(input); assert.equal(creates, 1);
  assert.equal((await current.service.get('game-session', order.orderId)).state, 'Created');
  await assert.rejects(current.service.confirm('game-session', order.orderId, 'client-success'), { code: 'PAY.VERIFICATION_FAILED' });
  const rawBody = Buffer.from(JSON.stringify({ client_key: binding.appId, event: 'minis.trade_order.redeem.success',
    create_time: timestamp, user_openid: 'platform-player', content: JSON.stringify({
      order_id: order.orderId, trade_order_id: 'platform-trade', is_sandbox: true }) }));
  const signature = `t=${timestamp},s=${createHmac('sha256', clientSecret).update(`${timestamp}.`).update(rawBody).digest('hex')}`;
  const serve = async payments => {
    const server = createServer(createTikTokPaymentWebhookHandler(payments, { binding, isSandbox: true,
      clientSecret, nowSeconds: () => timestamp, log: () => {} })); servers.push(server);
    await new Promise((resolve, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', resolve); });
    return async () => {
      const response = await fetch(`http://127.0.0.1:${server.address().port}/v1/webhooks/tiktok/payment`, {
        method: 'POST', headers: { 'Content-Type': 'application/json', 'TikTok-Signature': signature }, body: rawBody });
      assert.equal(response.status, 200); assert.equal((await response.json()).received, true);
    };
  };
  let notify = await serve(current.payments);
  await Promise.all([notify(), notify()]);
  assert.equal((await current.store.pendingDelivery()).length, 1);
  await assert.rejects(current.service.fulfill(order.orderId, async message => {
    grant(receiver, message); throw new Error('Receiver committed; acknowledgement lost');
  }), { code: 'PAY.DELIVERY_FAILED' });
  assert.equal(receiver.prepare('SELECT coins FROM wallet').get().coins, 100);
  current.store.close(); receiver.close(); current = open(); receiver = openReceiver();
  notify = await serve(current.payments); await notify();
  for (const pending of await current.store.pendingDelivery())
    await current.service.fulfill(pending.orderId, async message => grant(receiver, message));
  await notify();
  assert.equal((await current.service.get('game-session', order.orderId)).state, 'Fulfilled');
  assert.equal(receiver.prepare('SELECT coins FROM wallet').get().coins, 100);
  assert.equal(receiver.prepare('SELECT COUNT(*) AS count FROM deliveries').get().count, 1);
  assert.deepEqual(await current.store.pendingDelivery(), []); assert.equal(creates, 1);
});
