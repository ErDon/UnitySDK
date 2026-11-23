import { randomUUID } from 'node:crypto';
import { AuthError } from './auth.mjs';
import { OrderError } from './orders.mjs';
import { MemoryRateLimiter } from './rate-limit.mjs';

export function createOrderHandler(service, { log = console.info, bodyLimit = 8192, limiter = new MemoryRateLimiter(), prepayments = null } = {}) {
  if (!service || !Number.isSafeInteger(bodyLimit) || bodyLimit <= 0) throw new TypeError('Invalid order handler configuration');
  if (prepayments !== null && (prepayments.orders !== service || typeof prepayments.prepare !== 'function'))
    throw new TypeError('Prepayment service must use the same order service');
  return async (request, response) => {
    const callId = randomUUID();let operation = 'Order.Request', result = 'OK';
    response.setHeader('Content-Type', 'application/json; charset=utf-8');
    response.setHeader('Cache-Control', 'no-store');response.setHeader('X-Content-Type-Options', 'nosniff');
    const send = (status, data) => { response.writeHead(status);response.end(JSON.stringify({ ...data, callId })); };
    const readJson = async () => {
      if (request.headers['content-type']?.split(';')[0].trim().toLowerCase() !== 'application/json')
        throw new OrderError('REQUEST.CONTENT_TYPE', 415);
      let size = 0;const chunks = [];
      for await (const chunk of request.iterator({ destroyOnReturn: false })) {
        size += chunk.length;if (size > bodyLimit) throw new OrderError('REQUEST.TOO_LARGE', 413);
        chunks.push(chunk);
      }
      let value;
      try { value = JSON.parse(Buffer.concat(chunks).toString('utf8')); }
      catch { throw new OrderError('REQUEST.INVALID'); }
      if (!value || typeof value !== 'object' || Array.isArray(value)) throw new OrderError('REQUEST.INVALID');
      return value;
    };
    try {
      const allowance = await limiter.take(request.socket.remoteAddress);
      if (!allowance.allowed) {
        if (Number.isSafeInteger(allowance.retryAfterSeconds) && allowance.retryAfterSeconds > 0)
          response.setHeader('Retry-After', String(allowance.retryAfterSeconds));
        throw new OrderError('PAY.RATE_LIMITED', 429);
      }
      // Reject query-string tokens, alternate paths, and client-accessible fulfillment routes.
      const route = /^\/v1\/orders(?:\/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})(\/(?:confirm|prepare|query))?)?$/.exec(request.url ?? '');
      if (!route || (!route[1] && request.method !== 'POST') ||
          (route[1] && request.method !== (route[2] ? 'POST' : 'GET'))) throw new OrderError('REQUEST.NOT_FOUND', 404);
      if (route[2] === '/prepare' && !prepayments) throw new OrderError('REQUEST.NOT_FOUND', 404);
      if (route[2] === '/query' && typeof prepayments?.query !== 'function') throw new OrderError('REQUEST.NOT_FOUND', 404);
      const authorization = /^Bearer ([A-Za-z0-9_-]{43})$/.exec(request.headers.authorization ?? '');
      if (!authorization) throw new AuthError('AUTH.SESSION_INVALID');
      const session = authorization[1];let order;
      if (!route[1]) {
        operation = 'Order.Create';const input = await readJson();
        order = await service.create(session, { productId: input.productId, requestId: input.requestId });
      } else if (route[2] === '/query') {
        operation = 'Order.Query';
        await readJson();
        send(200, { query: await prepayments.query(session, route[1]) }); return;
      } else if (route[2] === '/prepare') {
        operation = 'Order.Prepare';
        await readJson(); // Body fields cannot supply prices, identity or platform tokens.
        const prepayment = await prepayments.prepare(session, route[1]);
        send(200, { prepayment }); return;
      } else if (route[2]) {
        operation = 'Order.Confirm';const input = await readJson();
        order = await service.confirm(session, route[1], input.evidence);
      } else {
        operation = 'Order.Get';order = await service.get(session, route[1]);
      }
      send(200, { order });
    } catch (error) {
      const known = error instanceof AuthError || error instanceof OrderError;
      result = known ? error.code : 'SERVER.INTERNAL';
      // Early rejection may leave an unread body; do not reuse this connection.
      response.setHeader('Connection', 'close');send(known ? error.status : 500, { errorCode: result });
    } finally {
      try { log(JSON.stringify({ time: new Date().toISOString(), interface: operation, callId,
        logId: randomUUID(), stage: 'OrderRequest', params: '[REDACTED]', result })); } catch { }
    }
  };
}
