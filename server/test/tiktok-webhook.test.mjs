import test from 'node:test';
import assert from 'node:assert/strict';
import { createHmac } from 'node:crypto';
import { verifyTikTokWebhook, readTikTokPaymentWebhook } from '../src/tiktok-webhook.mjs';

const rawBody = Buffer.from('{"event":"test", "content":"中文"}\n');
const clientSecret = 'local-test-secret';
const nowSeconds = 1800000000;
function signed(body = rawBody, timestamp = nowSeconds) {
  const digest = createHmac('sha256', clientSecret).update(Buffer.concat([Buffer.from(`${timestamp}.`), body])).digest('hex');
  return `t=${timestamp},s=${digest}`;
}
const verify = overrides => verifyTikTokWebhook({ rawBody, clientSecret, nowSeconds, signature: signed(), ...overrides });

test('valid raw UTF-8 bytes and reordered header are accepted without mutating inputs', () => {
  assert.deepEqual(verify(), { valid: true, timestamp: nowSeconds });
  assert.equal(verify({ signature: signed().split(',').reverse().join(', ') }).valid, true);
  assert.equal(Object.isFrozen(verify()), true);
});
test('body changes, JSON reserialization, wrong key and changed timestamp are rejected', () => {
  for (const overrides of [
    { rawBody: Buffer.from(JSON.stringify(JSON.parse(rawBody))) },
    { rawBody: Buffer.concat([rawBody, Buffer.from(' ')]) },
    { clientSecret: 'other-app-secret' },
    { signature: signed().replace(`t=${nowSeconds}`, `t=${nowSeconds + 1}`) }
  ]) assert.equal(verify(overrides).code, 'WEBHOOK.INVALID_SIGNATURE');
});
test('past and future timestamp window boundaries are enforced', () => {
  for (const delta of [-300, 300]) assert.equal(verify({ signature: signed(rawBody, nowSeconds + delta) }).valid, true);
  for (const delta of [-301, 301]) assert.equal(verify({ signature: signed(rawBody, nowSeconds + delta) }).code, 'WEBHOOK.TIMESTAMP_OUT_OF_RANGE');
  assert.equal(verify({ signature: signed(rawBody, nowSeconds - 1), toleranceSeconds: 0 }).valid, false);
});
test('ambiguous, missing, oversized and malformed headers fail closed', () => {
  for (const signature of [undefined, [], '', 't=1', 's=abc', signed() + ',t=1', signed() + ',s=00',
    signed() + ',extra=1', signed() + ',', 't=01,s=' + 'a'.repeat(64), 't=-1,s=' + 'a'.repeat(64),
    `t=${nowSeconds},s=${'z'.repeat(64)}`, ' '.repeat(513)])
    assert.equal(verify({ signature }).code, 'WEBHOOK.INVALID_SIGNATURE');
});
test('body input is bounded and configuration mistakes throw without secret exposure', () => {
  for (const body of [null, {}, rawBody.toString(), Buffer.alloc(0), Buffer.alloc(65537)])
    assert.equal(verify({ rawBody: body }).code, 'WEBHOOK.INVALID_BODY');
  const limit = Buffer.alloc(65536, 32);
  assert.equal(verify({ rawBody: limit, signature: signed(limit) }).valid, true);
  for (const options of [{ clientSecret: '' }, { nowSeconds: NaN }, { nowSeconds: -1 },
    { toleranceSeconds: -1 }, { toleranceSeconds: 301 }, { toleranceSeconds: 1.5 }])
    assert.throws(() => verify(options), { name: 'TypeError', message: 'Invalid webhook verifier configuration' });
});

const paymentContent = { order_id: 'game-order', trade_order_id: 'platform-order', is_sandbox: true };
const paymentEnvelope = { client_key: 'app-key', event: 'minis.trade_order.redeem.success',
  create_time: nowSeconds, user_openid: '', content: JSON.stringify(paymentContent) };
function readPayment(envelope = paymentEnvelope, overrides = {}) {
  const body = Buffer.isBuffer(envelope) ? envelope : Buffer.from(JSON.stringify(envelope));
  return readTikTokPaymentWebhook({ rawBody: body, signature: signed(body), clientSecret, nowSeconds,
    clientKey: 'app-key', isSandbox: true, ...overrides });
}
test('payment parsing exposes only immutable association fields after verification', () => {
  const result = readPayment({ ...paymentEnvelope, unknown: 'not-forwarded' });
  assert.equal(result.valid, true);
  assert.deepEqual(result.notification, { clientKey: 'app-key', isSandbox: true, orderId: 'game-order',
    tradeOrderId: 'platform-order', userOpenId: '', createdAtSeconds: nowSeconds });
  assert.equal(Object.isFrozen(result.notification), true);
  assert.equal(result.paid, undefined);
  assert.equal(readPayment({ ...paymentEnvelope, user_openid: 'user' }).notification.userOpenId, 'user');
});
test('correctly signed other app, sandbox and refund events cannot become payment success', () => {
  assert.equal(readPayment({ ...paymentEnvelope, client_key: 'other' }).code, 'WEBHOOK.APP_MISMATCH');
  assert.equal(readPayment(paymentEnvelope, { isSandbox: false }).code, 'WEBHOOK.ENVIRONMENT_MISMATCH');
  for (const event of ['minis.trade_order.redeem.refund_traceback', '', 'unrelated'])
    assert.equal(readPayment({ ...paymentEnvelope, event }).code, 'WEBHOOK.EVENT_UNSUPPORTED');
});
test('malformed signed payloads and incomplete identifiers fail closed', () => {
  for (const body of [Buffer.from('{'), Buffer.from([0xff]), Buffer.from('null'), Buffer.from('[]')])
    assert.equal(readPayment(body).code, 'WEBHOOK.INVALID_PAYLOAD');
  for (const patch of [{ content: {} }, { content: '[' }, { content: 'null' }, { create_time: 0.5 },
    { user_openid: null }, { user_openid: 'bad\nuser' }])
    assert.equal(readPayment({ ...paymentEnvelope, ...patch }).code, 'WEBHOOK.INVALID_PAYLOAD');
  for (const patch of [{ is_sandbox: 'true' }, { is_sandbox: null }, { order_id: '' },
    { trade_order_id: ' bad' }, { order_id: 'x'.repeat(257) }, { trade_order_id: 'bad\n' }])
    assert.equal(readPayment({ ...paymentEnvelope, content: JSON.stringify({ ...paymentContent, ...patch }) }).code, 'WEBHOOK.INVALID_PAYLOAD');
});
test('payment parser authenticates before decoding and requires explicit environment binding', () => {
  assert.equal(readPayment(Buffer.from('{'), { signature: signed() }).code, 'WEBHOOK.INVALID_SIGNATURE');
  for (const options of [{ isSandbox: undefined }, { isSandbox: 'false' }, { clientKey: '' }])
    assert.throws(() => readPayment(paymentEnvelope, options), { name: 'TypeError', message: 'Invalid payment webhook binding' });
});
