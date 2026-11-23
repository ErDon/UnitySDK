import { randomUUID } from 'node:crypto';
import { AuthError } from './auth.mjs';
import { MemoryRateLimiter } from './rate-limit.mjs';

export function createAuthHandler(service, { log = console.info, bodyLimit = 8192, limiter = new MemoryRateLimiter() } = {}) {
  return async (request, response) => {
    const callId = randomUUID();
    let result = 'OK';
    response.setHeader('Cache-Control', 'no-store');
    response.setHeader('Content-Type', 'application/json; charset=utf-8');
    response.setHeader('X-Content-Type-Options', 'nosniff');
    const send = (status, data) => { response.writeHead(status); response.end(JSON.stringify({ ...data, callId })); };
    try {
      // Do not trust client-supplied X-Forwarded-For. Deployments behind a proxy need a trusted gateway policy.
      const allowance = await limiter.take(request.socket.remoteAddress);
      if (!allowance.allowed) {
        response.setHeader('Connection', 'close');
        if (Number.isSafeInteger(allowance.retryAfterSeconds) && allowance.retryAfterSeconds > 0)
          response.setHeader('Retry-After', String(allowance.retryAfterSeconds));
        throw new AuthError('AUTH.RATE_LIMITED', 429);
      }
      if (request.method !== 'POST' || request.url !== '/v1/session') throw new AuthError('REQUEST.NOT_FOUND', 404);
      if (request.headers['content-type']?.split(';')[0].trim().toLowerCase() !== 'application/json') throw new AuthError('REQUEST.CONTENT_TYPE', 415);
      let size = 0; const chunks = [];
      // destroyOnReturn:false allows a bounded error response instead of destroying the socket.
      for await (const chunk of request.iterator({ destroyOnReturn: false })) {
        size += chunk.length;
        if (size > bodyLimit) {
          response.setHeader('Connection', 'close');
          throw new AuthError('REQUEST.TOO_LARGE', 413);
        }
        chunks.push(chunk);
      }
      let input;
      try { input = JSON.parse(Buffer.concat(chunks).toString('utf8')); }
      catch { throw new AuthError('REQUEST.INVALID', 400); }
      send(200, await service.exchange(input));
    } catch (error) {
      const known = error instanceof AuthError;
      result = known ? error.code : 'SERVER.INTERNAL';
      send(known ? error.status : 500, { errorCode: result });
    } finally {
      const line = JSON.stringify({ time: new Date().toISOString(), interface: 'Login.Exchange', callId,
        logId: randomUUID(), stage: 'SessionExchange', params: '[REDACTED]', result });
      try { log(line); } catch { /* Logging must not change authentication results. */ }
    }
  };
}
