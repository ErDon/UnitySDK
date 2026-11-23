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

