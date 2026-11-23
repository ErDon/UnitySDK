import { AuthError } from './auth.mjs';

export async function readLimitedJson(response, limit = 16384) {
  if (!response.body) throw new AuthError('AUTH.PLATFORM_RESPONSE_INVALID', 502);
  const reader = response.body.getReader();
  const chunks = []; let size = 0;
  try {
    for (;;) {
      const { done, value } = await reader.read();
      if (done) break;
      size += value.length;
      if (size > limit) throw new AuthError('AUTH.PLATFORM_RESPONSE_INVALID', 502);
      chunks.push(value);
    }
    return JSON.parse(Buffer.concat(chunks).toString('utf8'));
  } catch {
    throw new AuthError('AUTH.PLATFORM_RESPONSE_INVALID', 502);
  } finally { await reader.cancel().catch(() => {}); }
}

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
