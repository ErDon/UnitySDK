import { createHash, timingSafeEqual } from 'node:crypto';

const text = (value, max = 256) => typeof value === 'string' && value.length > 0 && value.length <= max &&
  value.trim() === value && !/[\u0000-\u001f\u007f]/.test(value);
const failed = code => Object.freeze({ valid: false, code });

// Mini-game virtual-payment protocol, NOT mini-app ecpay. Never reserialize msg before hashing.
export function verifyDouyinWebhook(envelope, callbackToken) {
  if (!text(callbackToken)) throw new TypeError('Invalid Douyin callback token');
  if (!envelope || Array.isArray(envelope) || typeof envelope !== 'object' ||
      typeof envelope.msg !== 'string' || Buffer.byteLength(envelope.msg, 'utf8') > 32768 ||
      !text(envelope.timestamp, 32) || !text(envelope.nonce) ||
      typeof envelope.signature !== 'string' || !/^[0-9a-f]{40}$/.test(envelope.signature))
    return failed('WEBHOOK.INVALID_SIGNATURE');
  // UTF-8 byte ordering matches the official Go example, including supplementary Unicode.
  const fields = [callbackToken, envelope.timestamp, envelope.nonce, envelope.msg].map(value => Buffer.from(value));
  fields.sort(Buffer.compare);
  const expected = createHash('sha1').update(Buffer.concat(fields)).digest();
  return timingSafeEqual(expected, Buffer.from(envelope.signature, 'hex'))
    ? Object.freeze({ valid: true }) : failed('WEBHOOK.INVALID_SIGNATURE');
}

export function readDouyinPaymentWebhook(envelope, { callbackToken, appId } = {}) {
  if (!text(appId)) throw new TypeError('Invalid Douyin app ID');
  const verified = verifyDouyinWebhook(envelope, callbackToken);
  if (!verified.valid) return verified;
  let message;
  try { message = JSON.parse(envelope.msg); } catch { return failed('WEBHOOK.INVALID_PAYLOAD'); }
  if (!message || Array.isArray(message) || message.appid !== appId ||
      ![message.cp_orderno, message.cp_extra, message.order_no_channel].every(value => text(value)) ||
      !Number.isSafeInteger(message.amount_cent) || message.amount_cent <= 0 ||
      !Number.isSafeInteger(message.amount_coin) || message.amount_coin < 0 ||
      !['CNY', 'DIAMOND'].includes(message.currency)) return failed('WEBHOOK.INVALID_PAYLOAD');
  return Object.freeze({ valid: true, notification: Object.freeze({
    appId: message.appid, orderId: message.cp_orderno, extraInfo: message.cp_extra,
    transactionId: message.order_no_channel, amountMinor: message.amount_cent,
    coinQuantity: message.amount_coin, currency: message.currency
  }) });
}
