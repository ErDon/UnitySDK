import { randomUUID } from 'node:crypto';
import { OrderError } from './orders.mjs';
import { AuthError } from './auth.mjs';
import { MemoryRateLimiter } from './rate-limit.mjs';

export function createTikTokPaymentWebhookHandler(payments, { binding, isSandbox, clientSecret,
  log = console.info, bodyLimit = 65536, limiter = new MemoryRateLimiter(), nowSeconds } = {}) {
  if (!payments || typeof payments.acceptWebhook !== 'function' || !binding || binding.platform !== 'TikTok' ||
      !['gameId', 'appId', 'environment'].every(k => typeof binding[k] === 'string' && binding[k].trim()) ||
      typeof isSandbox !== 'boolean' || typeof clientSecret !== 'string' || !clientSecret ||
      !Number.isSafeInteger(bodyLimit) || bodyLimit < 1 || bodyLimit > 65536 ||
      typeof log !== 'function' || typeof limiter?.take !== 'function' ||
      (nowSeconds !== undefined && typeof nowSeconds !== 'function')) throw new TypeError('Invalid TikTok webhook handler configuration');
  const scope = Object.freeze({ gameId: binding.gameId, appId: binding.appId, environment: binding.environment, platform: 'TikTok' });
  return async (request, response) => {
    const callId = randomUUID(); let result = 'OK';
    response.setHeader('Content-Type', 'application/json; charset=utf-8');
    response.setHeader('Cache-Control', 'no-store'); response.setHeader('X-Content-Type-Options', 'nosniff');
    const send = (status, value) => { response.writeHead(status); response.end(JSON.stringify({ ...value, callId })); };
    try {
      const allowance = await limiter.take(request.socket.remoteAddress);
      if (!allowance.allowed) {
        if (Number.isSafeInteger(allowance.retryAfterSeconds) && allowance.retryAfterSeconds > 0)
          response.setHeader('Retry-After', String(allowance.retryAfterSeconds));
        throw new OrderError('WEBHOOK.RATE_LIMITED', 429);
      }
      if (request.url !== '/v1/webhooks/tiktok/payment' || request.method !== 'POST')
        throw new OrderError('REQUEST.NOT_FOUND', 404);
      if (request.headers['content-type']?.split(';')[0].trim().toLowerCase() !== 'application/json' ||
          (request.headers['content-encoding'] && request.headers['content-encoding'] !== 'identity'))
        throw new OrderError('REQUEST.CONTENT_TYPE', 415);
      if (request.headersDistinct?.['tiktok-signature']?.length !== 1)
        throw new OrderError('WEBHOOK.INVALID_SIGNATURE', 401);
      const signature = request.headers['tiktok-signature'];
      if (typeof signature !== 'string' || signature.length > 512) throw new OrderError('WEBHOOK.INVALID_SIGNATURE', 401);
      const chunks = []; let size = 0;
      for await (const chunk of request.iterator({ destroyOnReturn: false })) {
        size += chunk.length;
        if (size > bodyLimit) throw new OrderError('REQUEST.TOO_LARGE', 413);
        chunks.push(chunk);
      }
      const accepted = await payments.acceptWebhook({ binding: scope, isSandbox, clientSecret, signature,
        rawBody: Buffer.concat(chunks), nowSeconds: nowSeconds?.() });
      if (!accepted.accepted) {
        const status = ['WEBHOOK.INVALID_SIGNATURE', 'WEBHOOK.TIMESTAMP_OUT_OF_RANGE'].includes(accepted.code) ? 401 : 400;
        throw new OrderError(accepted.code, status);
      }
      // Acknowledge only after the Paid transaction commits. No order/user data is returned.
      send(200, { received: true });
    } catch (error) {
      const known = error instanceof OrderError || error instanceof AuthError;
      result = known ? error.code : 'SERVER.INTERNAL';
      response.setHeader('Connection', 'close'); send(known ? error.status : 500, { errorCode: result });
    } finally {
      try { log(JSON.stringify({ time: new Date().toISOString(), interface: 'TikTok.PaymentWebhook',
        callId, logId: randomUUID(), stage: 'WebhookRequest', params: '[REDACTED]', result })); } catch { }
    }
  };
}
