import { createHmac, randomBytes } from 'node:crypto';
import { AuthError } from './auth.mjs';

// Per-process reference limiter; use a shared gateway/implementation for multi-instance deployments.
export class MemoryRateLimiter {
  constructor({ limit = 30, windowMs = 60000, maxKeys = 10000, now = Date.now } = {}) {
    for (const value of [limit, windowMs, maxKeys])
      if (!Number.isSafeInteger(value) || value <= 0) throw new TypeError('Invalid rate limit configuration');
    this.limit = limit; this.windowMs = windowMs; this.maxKeys = maxKeys; this.now = now;
    this.entries = new Map(); this.salt = randomBytes(32);
  }
  take(source) {
    const key = createHmac('sha256', this.salt).update(typeof source === 'string' && source ? source : 'unknown').digest('hex');
    const current = this.now();
    let entry = this.entries.get(key);
    if (entry && entry.resetAt <= current) { this.entries.delete(key); entry = null; }
    if (!entry) {
      if (this.entries.size >= this.maxKeys)
        for (const [id, item] of this.entries) if (item.resetAt <= current) this.entries.delete(id);
      if (this.entries.size >= this.maxKeys) throw new AuthError('SERVER.CAPACITY', 503);
      entry = { count: 0, resetAt: current + this.windowMs }; this.entries.set(key, entry);
    }
    if (entry.count >= this.limit)
      return { allowed: false, retryAfterSeconds: Math.max(1, Math.ceil((entry.resetAt - current) / 1000)) };
    entry.count++;
    return { allowed: true, retryAfterSeconds: 0 };
  }
}
