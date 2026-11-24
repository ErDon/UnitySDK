import { OrderError } from './orders.mjs';

const text = (v, max) => typeof v === 'string' && v.length > 0 && v.length <= max && !/\s/.test(v);
const record = v => v !== null && typeof v === 'object' && !Array.isArray(v);

// One client per application, never per player. Cache/coalescing is process-local.
export function createDouyinQueryClient({ binding, fetchImpl = fetch, now = Date.now, timeoutMs = 8000 } = {}) {
  if (binding?.platform !== 'Douyin' || !text(binding.appId, 256) || !text(binding.secret, 8192) ||
      typeof fetchImpl !== 'function' || typeof now !== 'function' || !Number.isSafeInteger(timeoutMs) || timeoutMs <= 0 || timeoutMs > 120000)
    throw new TypeError('Invalid Douyin query configuration');
  const appId = binding.appId, secret = binding.secret;
  let cached = null, refreshing = null;
  async function request(url, options, unavailable, invalid) {
    let response;
    try { response = await fetchImpl(url, { ...options, redirect: 'error', signal: AbortSignal.timeout(timeoutMs) }); }
    catch { throw new OrderError(unavailable, 502); }
    if (!response.ok) {
      try { await response.body?.cancel(); } catch { }
      throw new OrderError(unavailable, 502);
    }
    let reader;
    try {
      reader = response.body.getReader();
      const chunks = []; let size = 0;
      for (;;) {
        const { value, done } = await reader.read(); if (done) break;
        size += value.length; if (size > 16384) throw new Error(); chunks.push(value);
      }
      const result = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(Buffer.concat(chunks)));
      if (!record(result)) throw new Error();
      return result;
    } catch { throw new OrderError(invalid, 502); }
    finally { try { await reader?.cancel(); } catch { } }
  }
  async function accessToken() {
    const time = now();
    if (!Number.isSafeInteger(time) || time < 0) throw new OrderError('PAY.CREDENTIAL_UNAVAILABLE', 503);
    if (cached && time >= cached.createdAt && time < cached.refreshAt) return cached.token;
    if (refreshing) return refreshing;
    refreshing = (async () => {
      const result = await request('https://minigame.zijieapi.com/mgplatform/api/apps/v2/token', {
        method: 'POST', headers: { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' },
        body: JSON.stringify({ appid: appId, secret, grant_type: 'client_credential' })
      }, 'PAY.CREDENTIAL_UNAVAILABLE', 'PAY.CREDENTIAL_RESPONSE_INVALID');
      if (!Number.isSafeInteger(result.err_no)) throw new OrderError('PAY.CREDENTIAL_RESPONSE_INVALID', 502);
      if (result.err_no !== 0) throw new OrderError('PAY.PLATFORM_TOKEN_REJECTED', 502);
      if (!record(result.data) || !text(result.data.access_token, 8192) ||
          !Number.isSafeInteger(result.data.expires_in) || result.data.expires_in <= 60 || result.data.expires_in > 86400)
        throw new OrderError('PAY.CREDENTIAL_RESPONSE_INVALID', 502);
      // Start expiry at request time, refresh at most hourly, and retain a 60-second safety margin.
      cached = { token: result.data.access_token, createdAt: time,
        refreshAt: time + Math.min(3600, result.data.expires_in - 60) * 1000 };
      return cached.token;
    })();
    try { return await refreshing; } finally { refreshing = null; }
  }
  return Object.freeze({
    async query(orderId) {
      if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(orderId ?? ''))
        throw new TypeError('Invalid Douyin order ID');
      const token = await accessToken();
      const url = new URL('https://developer.toutiao.com/api/apps/game/payment/queryPayState');
      url.search = new URLSearchParams({ access_token: token, orderno: orderId });
      const result = await request(url, { method: 'GET', headers: { 'Cache-Control': 'no-store' } },
        'PAY.QUERY_UNAVAILABLE', 'PAY.QUERY_RESPONSE_INVALID');
      if ((result.err_no !== undefined && result.err_no !== 0) || (result.error !== undefined && result.error !== 0) ||
          !['success', 'unsuccess'].includes(result.status)) throw new OrderError('PAY.QUERY_RESPONSE_INVALID', 502);
      // No amount, transaction ID or payer identity is returned by this protocol.
      return Object.freeze({ status: result.status });
    }
  });
}
