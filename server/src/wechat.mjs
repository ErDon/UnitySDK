import { AuthError } from './auth.mjs';

import { readLimitedJson } from './platform-http.mjs';

// Fixed upstream: neither request bodies nor client configuration choose a URL.
export function createWeChatVerifier({ fetchImpl = fetch, timeoutMs = 8000 } = {}) {
  return async (binding, code) => {
    const url = new URL('https://api.weixin.qq.com/sns/jscode2session');
    url.search = new URLSearchParams({ appid: binding.appId, secret: binding.secret, js_code: code, grant_type: 'authorization_code' });
    try {
      const response = await fetchImpl(url, { redirect: 'error', signal: AbortSignal.timeout(timeoutMs) });
      if (!response.ok) { await response.body?.cancel(); throw new AuthError('AUTH.PLATFORM_UNAVAILABLE', 502); }
      const data = await readLimitedJson(response);
      if (!data || typeof data !== 'object' || Array.isArray(data)) throw new AuthError('AUTH.PLATFORM_RESPONSE_INVALID', 502);
      if (data.errcode !== undefined && data.errcode !== 0) throw new AuthError('AUTH.PLATFORM_REJECTED');
      if (typeof data.openid !== 'string' || !data.openid.trim() || typeof data.session_key !== 'string' || !data.session_key.trim())
        throw new AuthError('AUTH.PLATFORM_RESPONSE_INVALID', 502);
      // session_key/unionid are not client sessions and are not retained for this login capability.
      return { subject: data.openid };
    } catch (error) {
      if (error instanceof AuthError) throw error;
      // Never propagate fetch errors: they may contain the secret-bearing URL.
      throw new AuthError('AUTH.PLATFORM_UNAVAILABLE', 502);
    }
  };
}
