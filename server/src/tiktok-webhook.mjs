import { createHmac, timingSafeEqual } from 'node:crypto';

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
