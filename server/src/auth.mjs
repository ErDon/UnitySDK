import { createHash, randomBytes } from 'node:crypto';

export class AuthError extends Error {
  constructor(code, status = 401) { super(code); this.code = code; this.status = status; }
}
export const digest = value => createHash('sha256').update(value).digest('hex');
export const bindingKey = ({ gameId, appId, environment, platform }) =>
  JSON.stringify([gameId, appId, environment, platform]);

// Local development only. A deployment must supply shared atomic storage with TTL.
export class MemoryAuthStore {
  constructor({ now = Date.now, capacity = 10000 } = {}) {
    this.now = now; this.capacity = capacity; this.claims = new Map(); this.sessions = new Map();
  }
  prune(map) { for (const [key, value] of map) if (value.expiresAt <= this.now()) map.delete(key); }
  async claim(key, expiresAt) {
    this.prune(this.claims);
    if (this.claims.has(key)) return false;
    if (this.claims.size >= this.capacity) throw new AuthError('SERVER.CAPACITY', 503);
    this.claims.set(key, { expiresAt }); return true;
  }
  async putSession(hash, record) {
    this.prune(this.sessions);
    if (this.sessions.size >= this.capacity) throw new AuthError('SERVER.CAPACITY', 503);
    this.sessions.set(hash, Object.freeze({ ...record }));
  }
  async getSession(hash) {
    const value = this.sessions.get(hash);
    if (!value || value.expiresAt <= this.now()) { this.sessions.delete(hash); return null; }
    return { ...value };
  }
  async deleteSession(hash) { this.sessions.delete(hash); }
}

export class AuthService {
  constructor({ bindings, verifiers, store, now = Date.now, sessionTtlMs = 3600000, codeTtlMs = 600000 }) {
    if (!Array.isArray(bindings) || !store || !verifiers || !Number.isSafeInteger(sessionTtlMs) || sessionTtlMs <= 0 || !Number.isSafeInteger(codeTtlMs) || codeTtlMs <= 0)
      throw new Error('Invalid auth configuration');
    this.bindings = new Map();
    for (const binding of bindings) {
      for (const name of ['gameId', 'appId', 'environment', 'platform', 'secret'])
        if (typeof binding[name] !== 'string' || !binding[name].trim()) throw new Error('Invalid binding');
      const key = bindingKey(binding);
      if (this.bindings.has(key)) throw new Error('Duplicate binding');
      this.bindings.set(key, Object.freeze({ ...binding }));
    }
    this.verifiers = new Map(Object.entries(verifiers)); this.store = store; this.now = now;
    this.sessionTtlMs = sessionTtlMs; this.codeTtlMs = codeTtlMs;
  }
  async exchange(request) {
    if (!request || typeof request !== 'object' || Array.isArray(request)) throw new AuthError('REQUEST.INVALID', 400);
    for (const field of ['gameId', 'appId', 'environment', 'platform', 'code'])
      if (typeof request[field] !== 'string' || !request[field].trim() || request[field].length > 1024) throw new AuthError('REQUEST.INVALID', 400);
    const binding = this.bindings.get(bindingKey(request));
    if (!binding) throw new AuthError('AUTH.APP_NOT_ALLOWED', 403);
    const verify = this.verifiers.get(binding.platform);
    if (!verify) throw new AuthError('AUTH.PLATFORM_UNSUPPORTED', 400);
    // Platform codes belong to the application, not a single game/environment binding.
    const claimKey = digest(JSON.stringify([binding.platform, binding.appId, request.code]));
    if (!await this.store.claim(claimKey, this.now() + this.codeTtlMs)) throw new AuthError('AUTH.CODE_REUSED', 409);
    // Keep the claim even on transport failure: a platform may already have consumed the code.
    const identity = await verify(binding, request.code);
    if (!identity || typeof identity.subject !== 'string' || !identity.subject.trim()) throw new AuthError('AUTH.INVALID_IDENTITY', 502);
    const session = randomBytes(32).toString('base64url');
    const expiresAt = this.now() + this.sessionTtlMs;
    await this.store.putSession(digest(session), {
      binding: bindingKey(binding), subject: identity.subject, expiresAt
    });
    return { session, expiresAt };
  }
  async authenticate(session, expectedBinding) {
    if (typeof session !== 'string' || !/^[A-Za-z0-9_-]{43}$/.test(session)) throw new AuthError('AUTH.SESSION_INVALID');
    const record = await this.store.getSession(digest(session));
    if (!record || record.binding !== bindingKey(expectedBinding)) throw new AuthError('AUTH.SESSION_INVALID');
    return { subject: record.subject, expiresAt: record.expiresAt };
  }
  async revoke(session) {
    if (typeof session === 'string') await this.store.deleteSession(digest(session));
  }
}
