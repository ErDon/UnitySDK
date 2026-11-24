import { createHash, randomUUID, timingSafeEqual } from 'node:crypto';
import { OrderError } from './orders.mjs';
import { MemoryRateLimiter } from './rate-limit.mjs';

// Configure MP delivery push as JSON/plaintext over HTTPS. Payment payloads are
// independently authenticated with AppKey; XML/AES are explicitly rejected.
export function createWeChatPaymentWebhookHandler(payments, { callbackToken, deliver = null, log = console.info,
  limiter = new MemoryRateLimiter() } = {}) {
  if (typeof callbackToken !== 'string' || !/^[\x21-\x7e]{1,256}$/.test(callbackToken) || typeof payments?.acceptWebhook !== 'function')
    throw new TypeError('Invalid WeChat webhook configuration');
  return async (request, response) => {
    const callId = randomUUID(); let result = 'OK';
    response.setHeader('Cache-Control', 'no-store');
    const json = (status, value) => { response.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8' }); response.end(JSON.stringify(value)); };
    try {
      if (!(await limiter.take(request.socket.remoteAddress)).allowed) throw new OrderError('WEBHOOK.RATE_LIMITED', 429);
      if (request.url.length > 8192) throw new OrderError('REQUEST.INVALID');
      const url = new URL(request.url, 'http://localhost');
      if (url.pathname !== '/v1/webhooks/wechat/payment' || !['GET', 'POST'].includes(request.method)) throw new OrderError('REQUEST.NOT_FOUND', 404);
      const fields = ['signature', 'timestamp', 'nonce', 'echostr'];
      if ([...url.searchParams.keys()].some(x => !fields.includes(x)) || fields.some(x => url.searchParams.getAll(x).length > 1)) throw new OrderError('REQUEST.INVALID');
      const { signature, timestamp, nonce, echostr } = Object.fromEntries(url.searchParams);
      if (!/^[a-f0-9]{40}$/i.test(signature ?? '') || !/^\d{1,16}$/.test(timestamp ?? '') ||
          typeof nonce !== 'string' || !/^[\x21-\x7e]{1,256}$/.test(nonce)) throw new OrderError('WEBHOOK.INVALID_SIGNATURE', 401);
      const expected = createHash('sha1').update([callbackToken, timestamp, nonce].sort().join('')).digest();
      if (!timingSafeEqual(expected, Buffer.from(signature, 'hex'))) throw new OrderError('WEBHOOK.INVALID_SIGNATURE', 401);
      if (request.method === 'GET') {
        if (typeof echostr !== 'string' || !echostr || echostr.length > 1024) throw new OrderError('REQUEST.INVALID');
        response.writeHead(200, { 'Content-Type': 'text/plain; charset=utf-8' }); response.end(echostr); return;
      }
      if (request.headers['content-type']?.split(';')[0].trim().toLowerCase() !== 'application/json' ||
          (request.headers['content-encoding'] && request.headers['content-encoding'] !== 'identity')) throw new OrderError('REQUEST.CONTENT_TYPE', 415);
      let size = 0; const chunks = [];
      for await (const chunk of request.iterator({ destroyOnReturn: false })) {
        size += chunk.length; if (size > 65536) throw new OrderError('REQUEST.TOO_LARGE', 413); chunks.push(chunk);
      }
      let envelope;
      try { envelope = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(Buffer.concat(chunks))); }
      catch { throw new OrderError('REQUEST.INVALID'); }
      const accepted = await payments.acceptWebhook(envelope);
      if (!accepted.mock) {
        // Acknowledge delivery only after the game receiver durably grants once.
        // Paid survives failures; the worker or platform retry can finish delivery.
        if (accepted.state !== 'Fulfilled' && deliver) await payments.orders.fulfill(accepted.orderId, deliver);
        const order = await payments.store.get(accepted.orderId);
        if (order.state !== 'Fulfilled') throw new OrderError('PAY.DELIVERY_PENDING', 503);
      }
      json(200, { ErrCode: 0, ErrMsg: 'Success' });
    } catch (error) {
      const known = error instanceof OrderError; result = known ? error.code : 'SERVER.INTERNAL';
      response.setHeader('Connection', 'close'); json(known ? error.status : 500, { ErrCode: 1, ErrMsg: result });
    } finally {
      try { log(JSON.stringify({ time: new Date().toISOString(), interface: 'WeChat.PaymentWebhook', callId,
        logId: randomUUID(), stage: 'WebhookRequest', params: '[REDACTED]', result })); } catch {}
    }
  };
}
