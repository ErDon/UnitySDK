import { DatabaseSync } from 'node:sqlite';
import { createCipheriv, createDecipheriv, randomBytes } from 'node:crypto';
import { bindingKey } from './auth.mjs';
import { OrderError } from './orders.mjs';

const text = value => typeof value === 'string' && value.length > 0 && value.length <= 256 &&
  value.trim() === value && !/[\u0000-\u001f\u007f]/.test(value);
const token = value => typeof value === 'string' && /^[\x21-\x7e]{1,8192}$/.test(value);
const aad = (scope, subject, expiresAt, keyId) => Buffer.from(JSON.stringify(['TikTokCredential', 1, scope, subject, expiresAt, keyId]));

// Local durable vault. Encryption keys are supplied by deployment, never persisted here.
export class TikTokCredentialStore {
  #keys = new Map();
  constructor(path, { keys, currentKeyId, now = Date.now, capacity = 10000 } = {}) {
    if (typeof path !== 'string' || !path || !(keys instanceof Map) || !text(currentKeyId) ||
        !keys.has(currentKeyId) || typeof now !== 'function' || !Number.isSafeInteger(capacity) || capacity <= 0)
      throw new TypeError('Invalid credential store configuration');
    for (const [id, key] of keys) {
      if (!text(id) || !Buffer.isBuffer(key) || key.length !== 32) throw new TypeError('Invalid credential encryption key');
    }
    for (const [id, key] of keys) this.#keys.set(id, Buffer.from(key));
    this.currentKeyId = currentKeyId; this.now = now; this.capacity = capacity; this.closed = false;
    try {
      this.db = new DatabaseSync(path);
      this.db.exec('PRAGMA busy_timeout=5000; PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; BEGIN IMMEDIATE;');
      this.db.exec('CREATE TABLE IF NOT EXISTS erdon_tiktok_credential_schema(version INTEGER NOT NULL)');
      const versions = this.db.prepare('SELECT version FROM erdon_tiktok_credential_schema').all();
      if (versions.length > 1 || (versions.length && versions[0].version !== 1)) throw new Error();
      this.db.exec(`CREATE TABLE IF NOT EXISTS erdon_tiktok_credentials (
        binding TEXT NOT NULL, subject TEXT NOT NULL, expires_at INTEGER NOT NULL,
        key_id TEXT NOT NULL, nonce BLOB NOT NULL, ciphertext BLOB NOT NULL, tag BLOB NOT NULL,
        PRIMARY KEY(binding,subject));`);
      if (!versions.length) this.db.exec('INSERT INTO erdon_tiktok_credential_schema VALUES (1)');
      this.db.exec('COMMIT');
    } catch {
      try { this.db?.exec('ROLLBACK'); } catch { }
      try { this.db?.close(); } catch { }
      for (const key of this.#keys.values()) key.fill(0);
      this.#keys.clear(); this.closed = true;
      throw new OrderError('PAY.CREDENTIAL_UNAVAILABLE', 503);
    }
  }
  scope(binding, subject) {
    if (!binding || binding.platform !== 'TikTok' || !['gameId', 'appId', 'environment'].every(k => text(binding[k])) || !text(subject))
      throw new TypeError('Invalid credential binding');
    return bindingKey(binding);
  }
  transaction(action) {
    if (this.closed) throw new OrderError('PAY.CREDENTIAL_UNAVAILABLE', 503);
    try {
      this.db.exec('BEGIN IMMEDIATE'); const value = action(); this.db.exec('COMMIT'); return value;
    } catch {
      try { this.db.exec('ROLLBACK'); } catch { }
      throw new OrderError('PAY.CREDENTIAL_UNAVAILABLE', 503);
    }
  }
  time() { const value = this.now(); if (!Number.isSafeInteger(value) || value < 0) throw new TypeError('Invalid credential clock'); return value; }
  async put(binding, subject, { accessToken, expiresAt }) {
    const scope = this.scope(binding, subject), current = this.time();
    if (!token(accessToken) || !Number.isSafeInteger(expiresAt) || expiresAt <= current) throw new TypeError('Invalid credential value');
    this.transaction(() => {
      this.db.prepare('DELETE FROM erdon_tiktok_credentials WHERE expires_at<=?').run(current);
      const exists = this.db.prepare('SELECT 1 FROM erdon_tiktok_credentials WHERE binding=? AND subject=?').get(scope, subject);
      if (!exists && this.db.prepare('SELECT COUNT(*) AS count FROM erdon_tiktok_credentials').get().count >= this.capacity) throw new Error();
      const nonce = randomBytes(12), keyId = this.currentKeyId;
      const cipher = createCipheriv('aes-256-gcm', this.#keys.get(keyId), nonce);
      cipher.setAAD(aad(scope, subject, expiresAt, keyId));
      const ciphertext = Buffer.concat([cipher.update(accessToken, 'utf8'), cipher.final()]);
      this.db.prepare(`INSERT INTO erdon_tiktok_credentials VALUES (?,?,?,?,?,?,?)
        ON CONFLICT(binding,subject) DO UPDATE SET expires_at=excluded.expires_at,key_id=excluded.key_id,
        nonce=excluded.nonce,ciphertext=excluded.ciphertext,tag=excluded.tag`)
        .run(scope, subject, expiresAt, keyId, nonce, ciphertext, cipher.getAuthTag());
    });
  }
  async get(binding, subject) {
    const scope = this.scope(binding, subject), current = this.time();
    return this.transaction(() => {
      const row = this.db.prepare('SELECT * FROM erdon_tiktok_credentials WHERE binding=? AND subject=?').get(scope, subject);
      if (!row || row.expires_at <= current) return null;
      if (!Number.isSafeInteger(row.expires_at) || row.nonce.length !== 12 || row.tag.length !== 16 || row.ciphertext.length > 8192) throw new Error();
      const decipher = createDecipheriv('aes-256-gcm', this.#keys.get(row.key_id), row.nonce);
      decipher.setAAD(aad(scope, subject, row.expires_at, row.key_id)); decipher.setAuthTag(row.tag);
      const accessToken = Buffer.concat([decipher.update(row.ciphertext), decipher.final()]).toString('utf8');
      if (!token(accessToken)) throw new Error();
      return Object.freeze({ binding: scope, subject, accessToken, expiresAt: row.expires_at });
    });
  }
  async remove(binding, subject) {
    const scope = this.scope(binding, subject);
    this.transaction(() => this.db.prepare('DELETE FROM erdon_tiktok_credentials WHERE binding=? AND subject=?').run(scope, subject));
  }
  close() {
    if (this.closed) return;
    this.closed = true;
    try { this.db.close(); } finally { for (const key of this.#keys.values()) key.fill(0); this.#keys.clear(); }
  }
}
