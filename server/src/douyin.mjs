import { AuthError } from './auth.mjs';
import { readLimitedJson } from './platform-http.mjs';

// Mini GAME endpoint. Do not substitute the mini APP v2 endpoint or TikTok OAuth.
export function createDouyinVerifier({ fetchImpl = fetch, timeoutMs = 8000 } = {}) {
  return async (binding, code) => {
    const url = new URL('https://minigame.zijieapi.com/mgplatform/api/apps/jscode2session');
    url.search = new URLSearchParams({ appid: binding.appId, secret: binding.secret, code });
    try {
      const response = await fetchImpl(url, {
        method: 'GET', headers: { 'Content-Type': 'application/json' },
        redirect: 'error', signal: AbortSignal.timeout(timeoutMs)
      });
      if (!response.ok) { await response.body?.cancel(); throw new AuthError('AUTH.PLATFORM_UNAVAILABLE', 502); }
      const data = await readLimitedJson(response);
      if (!data || typeof data !== 'object' || Array.isArray(data) || !Number.isSafeInteger(data.error))
        throw new AuthError('AUTH.PLATFORM_RESPONSE_INVALID', 502);
      if (data.error !== 0) {
        const detail = Number.isSafeInteger(data.errcode) ? data.errcode : data.error;
        if ([40014, 40015, 40017].includes(detail)) throw new AuthError('AUTH.PLATFORM_CONFIGURATION', 502);
        if (detail === -1) throw new AuthError('AUTH.PLATFORM_UNAVAILABLE', 502);
        throw new AuthError('AUTH.PLATFORM_REJECTED');
      }
      if ((data.errcode !== undefined && data.errcode !== 0) || typeof data.openid !== 'string' || !data.openid.trim() ||
          typeof data.session_key !== 'string' || !data.session_key.trim())
        throw new AuthError('AUTH.PLATFORM_RESPONSE_INVALID', 502);
      // Account identity only: no anonymous fallback, no session_key disclosure.
      return { subject: data.openid };
    } catch (error) {
      if (error instanceof AuthError) throw error;
      throw new AuthError('AUTH.PLATFORM_UNAVAILABLE', 502);
    }
  };
}
