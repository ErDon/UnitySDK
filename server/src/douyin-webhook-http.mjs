import { randomUUID } from 'node:crypto';
import { OrderError } from './orders.mjs';
import { MemoryRateLimiter } from './rate-limit.mjs';
import { verifyDouyinWebhook } from './douyin-webhook.mjs';

export function createDouyinPaymentWebhookHandler(payments, { callbackToken, log = console.info,
  limiter = new MemoryRateLimiter(), bodyLimit = 65536 } = {}) {
  // Validate the secret without requiring a valid request.
  verifyDouyinWebhook(null, callbackToken);
  if (typeof payments?.acceptWebhook !== 'function' || typeof log !== 'function' || typeof limiter?.take !== 'function' ||
      !Number.isSafeInteger(bodyLimit) || bodyLimit < 1 || bodyLimit > 65536) throw new TypeError('Invalid Douyin webhook handler');
  return async (request, response) => {
    const callId = randomUUID(); let result = 'OK';
    response.setHeader('Cache-Control', 'no-store'); response.setHeader('X-Content-Type-Options', 'nosniff');
    const json = (status, value) => {
      response.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8' });
      response.end(JSON.stringify({ ...value, callId }));
    };
    try {
      const allowance = await limiter.take(request.socket.remoteAddress);
      if (!allowance.allowed) {
        if (Number.isSafeInteger(allowance.retryAfterSeconds) && allowance.retryAfterSeconds > 0)
          response.setHeader('Retry-After', String(allowance.retryAfterSeconds));
        throw new OrderError('WEBHOOK.RATE_LIMITED', 429);
      }
      const url = new URL(request.url, 'http://localhost');
      if (url.pathname !== '/v1/webhooks/douyin/payment' || !['GET', 'POST'].includes(request.method))
        throw new OrderError('REQUEST.NOT_FOUND', 404);
      if (request.method === 'GET') {
        const fields = ['signature', 'timestamp', 'nonce', 'msg', 'echostr'];
        if (request.url.length > 8192 || [...url.searchParams.keys()].some(key => !fields.includes(key)) ||
            fields.some(key => url.searchParams.getAll(key).length > 1)) throw new OrderError('REQUEST.INVALID', 400);
        const envelope = Object.fromEntries(url.searchParams); envelope.msg ??= '';
        if (!verifyDouyinWebhook(envelope, callbackToken).valid) throw new OrderError('WEBHOOK.INVALID_SIGNATURE', 401);
        if (typeof envelope.echostr !== 'string' || !envelope.echostr || envelope.echostr.length > 1024)
          throw new OrderError('REQUEST.INVALID', 400);
        response.writeHead(200, { 'Content-Type': 'text/plain; charset=utf-8' }); response.end(envelope.echostr); return;
      }
      if (url.search) throw new OrderError('REQUEST.NOT_FOUND', 404);
      if (request.headers['content-type']?.split(';')[0].trim().toLowerCase() !== 'application/json' ||
          (request.headers['content-encoding'] && request.headers['content-encoding'] !== 'identity'))
        throw new OrderError('REQUEST.CONTENT_TYPE', 415);
      const chunks = []; let size = 0;
      for await (const chunk of request.iterator({ destroyOnReturn: false })) {
        size += chunk.length;
        if (size > bodyLimit) throw new OrderError('REQUEST.TOO_LARGE', 413);
        chunks.push(chunk);
      }
      let envelope;
      try { envelope = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(Buffer.concat(chunks))); }
      catch { throw new OrderError('REQUEST.INVALID', 400); }
      const accepted = await payments.acceptWebhook(envelope, callbackToken);
      if (!accepted.accepted) throw new OrderError(accepted.code, accepted.code === 'WEBHOOK.INVALID_SIGNATURE' ? 401 : 400);
      json(200, { received: true }); // Paid has committed. Delivery runs separately.
    } catch (error) {
      const known = error instanceof OrderError;
      result = known ? error.code : 'SERVER.INTERNAL';
      response.setHeader('Connection', 'close'); json(known ? error.status : 500, { errorCode: result });
    } finally {
      try { log(JSON.stringify({ time: new Date().toISOString(), interface: 'Douyin.PaymentWebhook',
        callId, logId: randomUUID(), stage: 'WebhookRequest', params: '[REDACTED]', result })); } catch { }
    }
  };
}
