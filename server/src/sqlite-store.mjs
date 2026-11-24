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
      if (versions.length > 1 || (versions.length === 1 && ![1, 2].includes(versions[0].version))) throw new Error('Unsupported auth schema version');
      this.db.exec(`CREATE TABLE IF NOT EXISTS erdon_auth_claims (hash TEXT PRIMARY KEY, expires_at INTEGER NOT NULL);
        CREATE INDEX IF NOT EXISTS erdon_auth_claim_expiry ON erdon_auth_claims(expires_at);
        CREATE TABLE IF NOT EXISTS erdon_auth_sessions (hash TEXT PRIMARY KEY, binding TEXT NOT NULL, subject TEXT NOT NULL, expires_at INTEGER NOT NULL);
        CREATE INDEX IF NOT EXISTS erdon_auth_session_expiry ON erdon_auth_sessions(expires_at);`);
      // v2 is additive; older processes must be drained before opening this migrated database.
      this.db.exec(`CREATE TABLE IF NOT EXISTS erdon_auth_revision(id INTEGER PRIMARY KEY CHECK(id=1), revision INTEGER NOT NULL);
        INSERT OR IGNORE INTO erdon_auth_revision VALUES(1,0);
        CREATE TABLE IF NOT EXISTS erdon_auth_revocations(binding TEXT NOT NULL, subject TEXT NOT NULL, revision INTEGER NOT NULL,
          PRIMARY KEY(binding,subject));
        CREATE INDEX IF NOT EXISTS erdon_auth_session_subject ON erdon_auth_sessions(binding,subject);`);
      if (versions.length === 0) this.db.exec('INSERT INTO erdon_auth_schema VALUES (2)');
      else if (versions[0].version === 1) this.db.exec('UPDATE erdon_auth_schema SET version=2');
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
  async sessionRevision() {
    return this.transaction(() => this.db.prepare('SELECT revision FROM erdon_auth_revision WHERE id=1').get().revision);
  }
  async putSession(hash, record, revision = 0) {
    this.transaction(() => {
      const current = this.db.prepare('SELECT revision FROM erdon_auth_revision WHERE id=1').get().revision;
      if (!Number.isSafeInteger(revision) || revision < 0 || revision > current) throw new AuthError('SERVER.STORAGE_UNAVAILABLE', 503);
      const revoked = this.db.prepare('SELECT revision FROM erdon_auth_revocations WHERE binding=? AND subject=?').get(record.binding, record.subject);
      if (revoked && revoked.revision > revision) throw new AuthError('AUTH.LOGIN_REVOKED', 409);
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
  async revokeSubject(binding, subject) {
    this.transaction(() => {
      const exists = this.db.prepare('SELECT 1 FROM erdon_auth_revocations WHERE binding=? AND subject=?').get(binding, subject);
      const revision = this.db.prepare('SELECT revision FROM erdon_auth_revision WHERE id=1').get().revision;
      if ((!exists && this.db.prepare('SELECT COUNT(*) AS count FROM erdon_auth_revocations').get().count >= this.capacity) || revision >= Number.MAX_SAFE_INTEGER)
        throw new AuthError('SERVER.CAPACITY', 503);
      this.db.prepare('UPDATE erdon_auth_revision SET revision=? WHERE id=1').run(revision + 1);
      this.db.prepare(`INSERT INTO erdon_auth_revocations VALUES(?,?,?) ON CONFLICT(binding,subject) DO UPDATE SET revision=excluded.revision`)
        .run(binding, subject, revision + 1);
      this.db.prepare('DELETE FROM erdon_auth_sessions WHERE binding=? AND subject=?').run(binding, subject);
    });
  }
  close() { if (!this.closed) { this.db.close(); this.closed = true; } }
}
