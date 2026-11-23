import { AuthError } from './auth.mjs';
import { readLimitedJson } from './platform-http.mjs';

// TikTok Minis OAuth, not Login Kit web OAuth. SDK code is passed unchanged.
export function createTikTokVerifier({ fetchImpl = fetch, timeoutMs = 8000, credentialStore = null, now = Date.now } = {}) {
  if (typeof now !== 'function' || (credentialStore !== null && typeof credentialStore.put !== 'function'))
    throw new TypeError('Invalid TikTok verifier configuration');
  return async (binding, code) => {
    try {
      const startedAt = now();
      const response = await fetchImpl('https://open.tiktokapis.com/v2/oauth/token/', {
        method: 'POST', redirect: 'error', signal: AbortSignal.timeout(timeoutMs),
        headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'Cache-Control': 'no-cache' },
        body: new URLSearchParams({ client_key: binding.appId, client_secret: binding.secret,
          code, grant_type: 'authorization_code' }).toString()
      });
      if (response.status === 429 || response.status >= 500) {
        await response.body?.cancel();
        throw new AuthError('AUTH.PLATFORM_UNAVAILABLE', 502);
      }
      const data = await readLimitedJson(response);
      if (!data || typeof data !== 'object' || Array.isArray(data))
        throw new AuthError('AUTH.PLATFORM_RESPONSE_INVALID', 502);
      if (typeof data.error === 'string' && data.error) {
        // Never disclose error_description, token fields or upstream log contents.
        throw new AuthError('AUTH.PLATFORM_REJECTED');
      }
      if (!response.ok || data.error !== undefined || typeof data.open_id !== 'string' || !data.open_id.trim() ||
          typeof data.access_token !== 'string' || !data.access_token.trim() || data.token_type !== 'Bearer' ||
          !Number.isSafeInteger(data.expires_in) || data.expires_in <= 0)
        throw new AuthError('AUTH.PLATFORM_RESPONSE_INVALID', 502);
      if (credentialStore !== null) {
        // Count lifetime from before the request, never extend it by network latency.
        const expiresAt = startedAt + data.expires_in * 1000;
        const current = now();
        if (!Number.isSafeInteger(startedAt) || startedAt < 0 || !Number.isSafeInteger(current) || current < startedAt ||
            !Number.isSafeInteger(expiresAt) || expiresAt <= current)
          throw new AuthError('AUTH.PLATFORM_RESPONSE_INVALID', 502);
        try {
          await credentialStore.put(binding, data.open_id, { accessToken: data.access_token, expiresAt });
        } catch {
          // Do not issue a game session when enabled commerce credentials could not be saved.
          throw new AuthError('AUTH.CREDENTIAL_STORAGE_FAILED', 503);
        }
      }
      return { subject: data.open_id };
    } catch (error) {
      if (error instanceof AuthError) throw error;
      throw new AuthError('AUTH.PLATFORM_UNAVAILABLE', 502);
    }
  };
}
