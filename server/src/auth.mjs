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
    this.revision = 0; this.revocations = new Map();
  }
  prune(map) { for (const [key, value] of map) if (value.expiresAt <= this.now()) map.delete(key); }
  async claim(key, expiresAt) {
    this.prune(this.claims);
    if (this.claims.has(key)) return false;
    if (this.claims.size >= this.capacity) throw new AuthError('SERVER.CAPACITY', 503);
    this.claims.set(key, { expiresAt }); return true;
  }
  async sessionRevision() { return this.revision; }
  async putSession(hash, record, revision = 0) {
    if (!Number.isSafeInteger(revision) || revision < 0 || revision > this.revision) throw new AuthError('SERVER.STORAGE_UNAVAILABLE', 503);
    if ((this.revocations.get(JSON.stringify([record.binding, record.subject])) ?? 0) > revision)
      throw new AuthError('AUTH.LOGIN_REVOKED', 409);
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
  async revokeSubject(binding, subject) {
    const key = JSON.stringify([binding, subject]);
    if ((!this.revocations.has(key) && this.revocations.size >= this.capacity) || this.revision >= Number.MAX_SAFE_INTEGER)
      throw new AuthError('SERVER.CAPACITY', 503);
    this.revocations.set(key, ++this.revision);
    for (const [hash, record] of this.sessions)
      if (record.binding === binding && record.subject === subject) this.sessions.delete(hash);
  }
}

export class AuthService {
  constructor({ bindings, verifiers, store, now = Date.now, sessionTtlMs = 3600000, codeTtlMs = 600000, assertAuthorized = null }) {
    if (assertAuthorized !== null && typeof assertAuthorized !== 'function') throw new TypeError('Invalid authorization policy');
    this.assertAuthorized = assertAuthorized;
    if (!Array.isArray(bindings) || typeof store?.sessionRevision !== 'function' || typeof store?.revokeSubject !== 'function' || !verifiers || !Number.isSafeInteger(sessionTtlMs) || sessionTtlMs <= 0 || !Number.isSafeInteger(codeTtlMs) || codeTtlMs <= 0)
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
    // Snapshot before platform I/O. A later subject revocation rejects this login at commit.
    const revision = await this.store.sessionRevision();
    // Platform codes belong to the application, not a single game/environment binding.
    const claimKey = digest(JSON.stringify([binding.platform, binding.appId, request.code]));
    if (!await this.store.claim(claimKey, this.now() + this.codeTtlMs)) throw new AuthError('AUTH.CODE_REUSED', 409);
    // Keep the claim even on transport failure: a platform may already have consumed the code.
    const identity = await verify(binding, request.code);
    if (!identity || typeof identity.subject !== 'string' || !identity.subject.trim()) throw new AuthError('AUTH.INVALID_IDENTITY', 502);
    if (this.assertAuthorized) await this.assertAuthorized(binding, identity.subject);
    const session = randomBytes(32).toString('base64url');
    const expiresAt = this.now() + this.sessionTtlMs;
    await this.store.putSession(digest(session), {
      binding: bindingKey(binding), subject: identity.subject, expiresAt
    }, revision);
    return { session, expiresAt };
  }
  async authenticate(session, expectedBinding) {
    if (typeof session !== 'string' || !/^[A-Za-z0-9_-]{43}$/.test(session)) throw new AuthError('AUTH.SESSION_INVALID');
    const record = await this.store.getSession(digest(session));
    if (!record || record.binding !== bindingKey(expectedBinding)) throw new AuthError('AUTH.SESSION_INVALID');
    if (this.assertAuthorized) await this.assertAuthorized(expectedBinding, record.subject);
    return { subject: record.subject, expiresAt: record.expiresAt };
  }
  async revoke(session) {
    if (typeof session === 'string') await this.store.deleteSession(digest(session));
  }
  async revokeAll(session, expectedBinding) {
    if (!expectedBinding || typeof expectedBinding !== 'object' || Array.isArray(expectedBinding) ||
        !['gameId', 'appId', 'environment', 'platform'].every(key => typeof expectedBinding[key] === 'string' && expectedBinding[key].trim() && expectedBinding[key].length <= 1024))
      throw new AuthError('REQUEST.INVALID', 400);
    const binding = this.bindings.get(bindingKey(expectedBinding));
    if (!binding) throw new AuthError('AUTH.APP_NOT_ALLOWED', 403);
    const identity = await this.authenticate(session, binding);
    // Identity comes only from the authenticated session, never from request subject/userId.
    await this.store.revokeSubject(bindingKey(binding), identity.subject);
    return Object.freeze({ revoked: true, scope: 'game-sessions' });
  }
}
