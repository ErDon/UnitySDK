import { DatabaseSync } from 'node:sqlite';
import { AuthError } from './auth.mjs';

// Same-machine durable store. Do not place a WAL database on a network filesystem.
export class SqliteAuthStore {
  constructor(path, { now = Date.now, capacity = 100000 } = {}) {
    if (typeof path !== 'string' || !path || !Number.isSafeInteger(capacity) || capacity <= 0) throw new Error('Invalid store configuration');
    this.now = now; this.capacity = capacity; this.closed = false;
    this.db = new DatabaseSync(path);
    try {
      this.db.exec('PRAGMA busy_timeout=5000; PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;');
      this.db.exec('BEGIN IMMEDIATE');
      this.db.exec('CREATE TABLE IF NOT EXISTS erdon_auth_schema (version INTEGER NOT NULL)');
      const versions = this.db.prepare('SELECT version FROM erdon_auth_schema').all();
      if (versions.length > 1 || (versions.length === 1 && versions[0].version !== 1)) throw new Error('Unsupported auth schema version');
      this.db.exec(`CREATE TABLE IF NOT EXISTS erdon_auth_claims (hash TEXT PRIMARY KEY, expires_at INTEGER NOT NULL);
        CREATE INDEX IF NOT EXISTS erdon_auth_claim_expiry ON erdon_auth_claims(expires_at);
        CREATE TABLE IF NOT EXISTS erdon_auth_sessions (hash TEXT PRIMARY KEY, binding TEXT NOT NULL, subject TEXT NOT NULL, expires_at INTEGER NOT NULL);
        CREATE INDEX IF NOT EXISTS erdon_auth_session_expiry ON erdon_auth_sessions(expires_at);`);
      if (versions.length === 0) this.db.exec('INSERT INTO erdon_auth_schema VALUES (1)');
      this.db.exec('COMMIT');
    } catch {
      try { this.db.exec('ROLLBACK'); } catch { }
      this.db.close(); this.closed = true;
      throw new Error('Cannot initialize auth database; verify path, permissions and schema version');
    }
  }
  transaction(action) {
    if (this.closed) throw new AuthError('SERVER.STORAGE_UNAVAILABLE', 503);
    try {
      this.db.exec('BEGIN IMMEDIATE');
      const result = action(); this.db.exec('COMMIT'); return result;
    } catch (error) {
      try { this.db.exec('ROLLBACK'); } catch { }
      if (error instanceof AuthError) throw error;
      throw new AuthError('SERVER.STORAGE_UNAVAILABLE', 503);
    }
  }
  async claim(hash, expiresAt) {
    return this.transaction(() => {
      this.db.prepare('DELETE FROM erdon_auth_claims WHERE expires_at <= ?').run(this.now());
      if (this.db.prepare('SELECT 1 FROM erdon_auth_claims WHERE hash = ?').get(hash)) return false;
      if (this.db.prepare('SELECT COUNT(*) AS count FROM erdon_auth_claims').get().count >= this.capacity) throw new AuthError('SERVER.CAPACITY', 503);
      this.db.prepare('INSERT INTO erdon_auth_claims VALUES (?, ?)').run(hash, expiresAt);
      return true;
    });
  }
  async putSession(hash, record) {
    this.transaction(() => {
      this.db.prepare('DELETE FROM erdon_auth_sessions WHERE expires_at <= ?').run(this.now());
      if (this.db.prepare('SELECT COUNT(*) AS count FROM erdon_auth_sessions').get().count >= this.capacity) throw new AuthError('SERVER.CAPACITY', 503);
      this.db.prepare('INSERT INTO erdon_auth_sessions VALUES (?, ?, ?, ?)').run(hash, record.binding, record.subject, record.expiresAt);
    });
  }
  async getSession(hash) {
    return this.transaction(() => {
      this.db.prepare('DELETE FROM erdon_auth_sessions WHERE expires_at <= ?').run(this.now());
      const row = this.db.prepare('SELECT binding, subject, expires_at AS expiresAt FROM erdon_auth_sessions WHERE hash = ?').get(hash);
      return row ? { ...row } : null;
    });
  }
  async deleteSession(hash) {
    this.transaction(() => this.db.prepare('DELETE FROM erdon_auth_sessions WHERE hash = ?').run(hash));
  }
  close() { if (!this.closed) { this.db.close(); this.closed = true; } }
}
