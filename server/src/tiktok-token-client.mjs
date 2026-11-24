import { AuthError } from './auth.mjs';
import { readLimitedJson } from './platform-http.mjs';
const text = value => typeof value === 'string' && value.trim() === value && value.length > 0 && value.length <= 8192 && !/[\u0000-\u0020\u007f]/.test(value);

// Server-only token lifecycle. Never expose the returned token material to Unity.
export function createTikTokTokenClient({ fetchImpl = fetch, timeoutMs = 8000, now = Date.now } = {}) {
  if (typeof fetchImpl !== 'function' || typeof now !== 'function' || !Number.isSafeInteger(timeoutMs) || timeoutMs < 1 || timeoutMs > 120000)
    throw new TypeError('Invalid token client configuration');
  const request = async (binding, path, fields) => {
    if (binding?.platform !== 'TikTok' || !text(binding.appId) || !text(binding.secret)) throw new TypeError('Invalid TikTok binding');
    try {
      const response = await fetchImpl('https://open.tiktokapis.com/v2/oauth/' + path + '/', {
        method: 'POST', redirect: 'error', signal: AbortSignal.timeout(timeoutMs),
        headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'Cache-Control': 'no-store' },
        body: new URLSearchParams({ client_key: binding.appId, client_secret: binding.secret, ...fields }).toString()
      });
      if (!response.ok) {
        try { await response.body?.cancel(); } catch { }
        throw new AuthError(response.status === 400 || response.status === 401 ? 'AUTH.PLATFORM_REJECTED' : 'AUTH.PLATFORM_UNAVAILABLE', response.status === 400 || response.status === 401 ? 401 : 502);
      }
      return response;
    } catch (error) { if (error instanceof AuthError) throw error; throw new AuthError('AUTH.PLATFORM_UNAVAILABLE', 502); }
  };
  return Object.freeze({
    async refresh(binding, subject, refreshToken) {
      if (!text(subject) || !text(refreshToken)) throw new TypeError('Invalid refresh request');
      const started = now();
      const response = await request(binding, 'token', { grant_type: 'refresh_token', refresh_token: refreshToken });
      const data = await readLimitedJson(response), current = now();
      if (!data || typeof data !== 'object' || Array.isArray(data) || data.error !== undefined || data.open_id !== subject ||
          data.token_type !== 'Bearer' || !text(data.access_token) || !text(data.refresh_token) ||
          !Number.isSafeInteger(data.expires_in) || data.expires_in <= 0 ||
          !Number.isSafeInteger(data.refresh_expires_in) || data.refresh_expires_in <= 0)
        throw new AuthError('AUTH.PLATFORM_RESPONSE_INVALID', 502);
      const expiresAt = started + data.expires_in * 1000, refreshExpiresAt = started + data.refresh_expires_in * 1000;
      if (!Number.isSafeInteger(started) || started < 0 || !Number.isSafeInteger(current) || current < started ||
          !Number.isSafeInteger(expiresAt) || expiresAt <= current || !Number.isSafeInteger(refreshExpiresAt) || refreshExpiresAt <= current)
        throw new AuthError('AUTH.PLATFORM_RESPONSE_INVALID', 502);
      return Object.freeze({ subject, accessToken: data.access_token, expiresAt, refreshToken: data.refresh_token, refreshExpiresAt });
    },
    async revoke(binding, accessToken) {
      if (!text(accessToken)) throw new TypeError('Invalid revoke request');
      const response = await request(binding, 'revoke', { token: accessToken });
      // Official success has an empty response struct. Accept empty body or empty JSON object only.
      let reader;
      try {
        if (response.body === null) return;
        reader = response.body.getReader(); let length = 0; const chunks = [];
        for (;;) { const { done, value } = await reader.read(); if (done) break;
          length += value.length; if (length > 16384) throw new Error(); chunks.push(value); }
        const body = new TextDecoder('utf-8', { fatal: true }).decode(Buffer.concat(chunks));
        if (!body.trim()) return;
        const data = JSON.parse(body);
        if (!data || typeof data !== 'object' || Array.isArray(data) || Object.keys(data).length) throw new Error();
      } catch { throw new AuthError('AUTH.PLATFORM_RESPONSE_INVALID', 502); }
      finally { try { await reader?.cancel(); } catch { } }
    }
  });
}
