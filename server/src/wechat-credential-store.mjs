import { DatabaseSync } from 'node:sqlite';
import { createCipheriv, createDecipheriv, randomBytes } from 'node:crypto';
import { bindingKey } from './auth.mjs';
import { OrderError } from './orders.mjs';

const text = x => typeof x === 'string' && /^[\x21-\x7e]{1,256}$/.test(x);
const aad = (scope, subject, expires, key) => Buffer.from(JSON.stringify(['WeChatSession', 1, scope, subject, expires, key]));

// session_key never leaves the backend. The local retention limit is NOT a claim
// about WeChat's undisclosed session lifetime; upstream expiry requires login again.
export class WeChatCredentialStore {
  #keys = new Map();
  constructor(path, { keys, currentKeyId, now = Date.now, maxAgeMs = 7200000, capacity = 10000 } = {}) {
    if (!text(currentKeyId) || !(keys instanceof Map) || !keys.has(currentKeyId) || typeof now !== 'function' ||
        !Number.isSafeInteger(maxAgeMs) || maxAgeMs <= 0 || !Number.isSafeInteger(capacity) || capacity <= 0)
      throw new TypeError('Invalid WeChat credential configuration');
    for (const [id, key] of keys) {
      if (!text(id) || !Buffer.isBuffer(key) || key.length !== 32) throw new TypeError('Invalid credential key');
      this.#keys.set(id, Buffer.from(key));
    }
    this.currentKeyId = currentKeyId; this.now = now; this.maxAgeMs = maxAgeMs; this.capacity = capacity;
    try {
      this.db = new DatabaseSync(path);
      this.db.exec(`PRAGMA busy_timeout=5000; PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; BEGIN IMMEDIATE;
        CREATE TABLE IF NOT EXISTS erdon_wechat_credential_schema(version INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS erdon_wechat_credentials(binding TEXT NOT NULL, subject TEXT NOT NULL,
          expires_at INTEGER NOT NULL, key_id TEXT NOT NULL, nonce BLOB NOT NULL, ciphertext BLOB NOT NULL, tag BLOB NOT NULL,
          PRIMARY KEY(binding,subject));`);
      const rows = this.db.prepare('SELECT version FROM erdon_wechat_credential_schema').all();
      if (rows.length > 1 || (rows.length && rows[0].version !== 1)) throw new Error();
      if (!rows.length) this.db.exec('INSERT INTO erdon_wechat_credential_schema VALUES(1)');
      this.db.exec('COMMIT');
    } catch { try { this.db?.exec('ROLLBACK'); } catch {} this.close(); throw new OrderError('PAY.CREDENTIAL_UNAVAILABLE', 503); }
  }
  scope(binding, subject) {
    if (binding?.platform !== 'WeChat' || !['gameId', 'appId', 'environment'].every(k => text(binding[k])) || !text(subject))
      throw new TypeError('Invalid WeChat credential binding');
    return bindingKey(binding);
  }
  transaction(action) {
    try { this.db.exec('BEGIN IMMEDIATE'); const value = action(); this.db.exec('COMMIT'); return value; }
    catch { try { this.db.exec('ROLLBACK'); } catch {} throw new OrderError('PAY.CREDENTIAL_UNAVAILABLE', 503); }
  }
  async put(binding, subject, sessionKey) {
    const scope = this.scope(binding, subject), now = this.now(), expires = now + this.maxAgeMs;
    if (!text(sessionKey) || !Number.isSafeInteger(now) || now < 0 || !Number.isSafeInteger(expires)) throw new TypeError('Invalid session key');
    this.transaction(() => {
      this.db.prepare('DELETE FROM erdon_wechat_credentials WHERE expires_at<=?').run(now);
      const exists = this.db.prepare('SELECT 1 FROM erdon_wechat_credentials WHERE binding=? AND subject=?').get(scope, subject);
      if (!exists && this.db.prepare('SELECT COUNT(*) AS n FROM erdon_wechat_credentials').get().n >= this.capacity) throw new Error();
      const key = this.currentKeyId, nonce = randomBytes(12), cipher = createCipheriv('aes-256-gcm', this.#keys.get(key), nonce);
      cipher.setAAD(aad(scope, subject, expires, key));
      const bytes = Buffer.concat([cipher.update(sessionKey, 'utf8'), cipher.final()]);
      this.db.prepare(`INSERT INTO erdon_wechat_credentials VALUES(?,?,?,?,?,?,?) ON CONFLICT(binding,subject)
        DO UPDATE SET expires_at=excluded.expires_at,key_id=excluded.key_id,nonce=excluded.nonce,ciphertext=excluded.ciphertext,tag=excluded.tag`)
        .run(scope, subject, expires, key, nonce, bytes, cipher.getAuthTag());
    });
  }
  async get(binding, subject) {
    const scope = this.scope(binding, subject);
    return this.transaction(() => {
      const row = this.db.prepare('SELECT * FROM erdon_wechat_credentials WHERE binding=? AND subject=?').get(scope, subject);
      if (!row || row.expires_at <= this.now()) return null;
      if (row.nonce.length !== 12 || row.tag.length !== 16 || row.ciphertext.length > 256) throw new Error();
      const decipher = createDecipheriv('aes-256-gcm', this.#keys.get(row.key_id), row.nonce);
      decipher.setAAD(aad(scope, subject, row.expires_at, row.key_id)); decipher.setAuthTag(row.tag);
      const sessionKey = Buffer.concat([decipher.update(row.ciphertext), decipher.final()]).toString('utf8');
      if (!text(sessionKey)) throw new Error();
      return { sessionKey };
    });
  }
  close() { try { this.db?.close(); } catch {} for (const key of this.#keys.values()) key.fill(0); this.#keys.clear(); }
}
