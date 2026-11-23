import { createHmac, timingSafeEqual } from 'node:crypto';

const validIdentifier = value => typeof value === 'string' && value.length > 0 && value.length <= 256 &&
  value.trim() === value && !/[\u0000-\u001f\u007f]/.test(value);
const record = value => value !== null && typeof value === 'object' && !Array.isArray(value);

// Verifies transport authenticity only. Callers must still validate app/order/environment
// and atomically deduplicate delivery. Never pass a parsed/re-serialized request body.
export function verifyTikTokWebhook({ rawBody, signature, clientSecret, nowSeconds = Math.floor(Date.now() / 1000), toleranceSeconds = 300 } = {}) {
  if (typeof clientSecret !== 'string' || clientSecret.length === 0 ||
      !Number.isSafeInteger(nowSeconds) || nowSeconds < 0 ||
      !Number.isSafeInteger(toleranceSeconds) || toleranceSeconds < 0 || toleranceSeconds > 300)
    throw new TypeError('Invalid webhook verifier configuration');
  const fail = code => Object.freeze({ valid: false, code });
  if (!Buffer.isBuffer(rawBody) || rawBody.length === 0 || rawBody.length > 65536)
    return fail('WEBHOOK.INVALID_BODY');
  if (typeof signature !== 'string' || signature.length > 512)
    return fail('WEBHOOK.INVALID_SIGNATURE');
  const fields = new Map();
  for (const part of signature.split(',')) {
    const match = /^\s*([ts])=([^\s,=]+)\s*$/.exec(part);
    if (!match || fields.has(match[1])) return fail('WEBHOOK.INVALID_SIGNATURE');
    fields.set(match[1], match[2]);
  }
  const timestamp = fields.get('t'), digest = fields.get('s');
  if (!/^(0|[1-9][0-9]{0,15})$/.test(timestamp ?? '') || !/^[a-fA-F0-9]{64}$/.test(digest ?? ''))
    return fail('WEBHOOK.INVALID_SIGNATURE');
  const seconds = Number(timestamp);
  if (!Number.isSafeInteger(seconds)) return fail('WEBHOOK.INVALID_SIGNATURE');
  const expected = createHmac('sha256', clientSecret).update(timestamp + '.', 'utf8').update(rawBody).digest();
  if (!timingSafeEqual(expected, Buffer.from(digest, 'hex'))) return fail('WEBHOOK.INVALID_SIGNATURE');
  if (Math.abs(nowSeconds - seconds) > toleranceSeconds) return fail('WEBHOOK.TIMESTAMP_OUT_OF_RANGE');
  return Object.freeze({ valid: true, timestamp: seconds });
}

// This does not authorize fulfillment. Match the returned IDs against a trusted,
// persisted prepayment association before any state transition.
export function readTikTokPaymentWebhook({ clientKey, isSandbox, ...verification } = {}) {
  if (!validIdentifier(clientKey) || typeof isSandbox !== 'boolean')
    throw new TypeError('Invalid payment webhook binding');
  const checked = verifyTikTokWebhook(verification);
  if (!checked.valid) return checked;
  const fail = code => Object.freeze({ valid: false, code });
  let envelope, content;
  try {
    // Reject malformed UTF-8 rather than silently replacing signed bytes.
    envelope = JSON.parse(new TextDecoder('utf-8', { fatal: true, ignoreBOM: true }).decode(verification.rawBody));
    if (!record(envelope) || typeof envelope.content !== 'string') return fail('WEBHOOK.INVALID_PAYLOAD');
    content = JSON.parse(envelope.content);
  } catch { return fail('WEBHOOK.INVALID_PAYLOAD'); }
  if (!record(content) || !Number.isSafeInteger(envelope.create_time) || envelope.create_time < 0 ||
      typeof envelope.user_openid !== 'string' ||
      (envelope.user_openid !== '' && !validIdentifier(envelope.user_openid)) ||
      !validIdentifier(content.order_id) || !validIdentifier(content.trade_order_id) ||
      typeof content.is_sandbox !== 'boolean') return fail('WEBHOOK.INVALID_PAYLOAD');
  if (envelope.client_key !== clientKey) return fail('WEBHOOK.APP_MISMATCH');
  if (content.is_sandbox !== isSandbox) return fail('WEBHOOK.ENVIRONMENT_MISMATCH');
  if (envelope.event !== 'minis.trade_order.redeem.success') return fail('WEBHOOK.EVENT_UNSUPPORTED');
  return Object.freeze({ valid: true, timestamp: checked.timestamp, notification: Object.freeze({
    clientKey, isSandbox, orderId: content.order_id, tradeOrderId: content.trade_order_id,
    // Official examples allow empty user_openid; ownership must come from the prepayment association.
    userOpenId: envelope.user_openid, createdAtSeconds: envelope.create_time
  }) });
}
