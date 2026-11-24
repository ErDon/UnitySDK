import { DatabaseSync } from 'node:sqlite';
import { createCipheriv, createDecipheriv, randomBytes } from 'node:crypto';
import { bindingKey } from './auth.mjs';
import { OrderError } from './orders.mjs';

const text = value => typeof value === 'string' && value.length > 0 && value.length <= 256 &&
  value.trim() === value && !/[\u0000-\u001f\u007f]/.test(value);
const token = value => typeof value === 'string' && /^[\x21-\x7e]{1,8192}$/.test(value);
const aad = (scope, subject, expiresAt, keyId, refresh = false) => Buffer.from(JSON.stringify([refresh ? 'TikTokRefreshCredential' : 'TikTokCredential', 1, scope, subject, expiresAt, keyId]));

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
      if (versions.length > 1 || (versions.length && ![1, 2, 3, 4].includes(versions[0].version))) throw new Error();
      this.db.exec(`CREATE TABLE IF NOT EXISTS erdon_tiktok_credentials (
        binding TEXT NOT NULL, subject TEXT NOT NULL, expires_at INTEGER NOT NULL,
        key_id TEXT NOT NULL, nonce BLOB NOT NULL, ciphertext BLOB NOT NULL, tag BLOB NOT NULL,
        PRIMARY KEY(binding,subject));`);
      this.db.exec(`CREATE TABLE IF NOT EXISTS erdon_tiktok_refresh_credentials (
        binding TEXT NOT NULL, subject TEXT NOT NULL, expires_at INTEGER NOT NULL,
        key_id TEXT NOT NULL, nonce BLOB NOT NULL, ciphertext BLOB NOT NULL, tag BLOB NOT NULL,
        PRIMARY KEY(binding,subject));`);
      this.db.exec(`CREATE TABLE IF NOT EXISTS erdon_tiktok_authorization_revision(id INTEGER PRIMARY KEY CHECK(id=1), revision INTEGER NOT NULL);
        INSERT OR IGNORE INTO erdon_tiktok_authorization_revision VALUES(1,0);
        CREATE TABLE IF NOT EXISTS erdon_tiktok_revocations(binding TEXT NOT NULL, subject TEXT NOT NULL,
          job_id TEXT NOT NULL, state TEXT NOT NULL, revision INTEGER NOT NULL, PRIMARY KEY(binding,subject));`);
      this.db.exec(`CREATE TABLE IF NOT EXISTS erdon_tiktok_refresh_claims(binding TEXT NOT NULL, subject TEXT NOT NULL,
        revision TEXT NOT NULL, owner TEXT NOT NULL, state TEXT NOT NULL, PRIMARY KEY(binding,subject));`);
      this.db.exec(`CREATE TABLE IF NOT EXISTS erdon_tiktok_revocation_evidence(job_id TEXT PRIMARY KEY,
        evidence_id TEXT NOT NULL UNIQUE, envelope_hash TEXT NOT NULL, reviewer TEXT NOT NULL, source_reference TEXT NOT NULL,
        source_sha256 TEXT NOT NULL, recorded_at INTEGER NOT NULL);`);
      if (!versions.length) this.db.exec('INSERT INTO erdon_tiktok_credential_schema VALUES (4)');
      else if (versions[0].version < 4) this.db.exec('UPDATE erdon_tiktok_credential_schema SET version=4');
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
  async put(binding, subject, { accessToken, expiresAt, refreshToken = null, refreshExpiresAt = null }, expectedRefreshRevision = null, authorizationRevision = 0, refreshOwner = null) {
    const scope = this.scope(binding, subject), current = this.time();
    if (!token(accessToken) || !Number.isSafeInteger(expiresAt) || expiresAt <= current) throw new TypeError('Invalid credential value');
    if (refreshToken !== null && (!token(refreshToken) || !Number.isSafeInteger(refreshExpiresAt) || refreshExpiresAt <= current)) throw new TypeError('Invalid refresh credential');
    return this.transaction(() => {
      if (expectedRefreshRevision === null && (!Number.isSafeInteger(authorizationRevision) || authorizationRevision < 0 ||
          authorizationRevision > this.db.prepare('SELECT revision FROM erdon_tiktok_authorization_revision WHERE id=1').get().revision)) throw new Error();
      const revoke = this.revocationRow(scope, subject);
      if (revoke && (revoke.state !== 'Complete' || (expectedRefreshRevision === null && revoke.revision > authorizationRevision))) throw new Error();
      if (expectedRefreshRevision !== null) {
        const previous = this.db.prepare('SELECT nonce FROM erdon_tiktok_refresh_credentials WHERE binding=? AND subject=?').get(scope, subject);
        if (!previous || Buffer.from(previous.nonce).toString('base64') !== expectedRefreshRevision) return false;
        const claim = this.db.prepare('SELECT owner,state FROM erdon_tiktok_refresh_claims WHERE binding=? AND subject=?').get(scope, subject);
        if (claim && (claim.owner !== refreshOwner || claim.state !== 'Sending')) throw new Error();
      }
      // Keep encrypted material for unresolved revocation, even if a different user's login prunes expired rows.
      for (const table of ['erdon_tiktok_credentials', 'erdon_tiktok_refresh_credentials'])
        this.db.prepare(`DELETE FROM ${table} WHERE expires_at<=? AND NOT EXISTS
          (SELECT 1 FROM erdon_tiktok_revocations r WHERE json_extract(r.binding,'$[1]')=json_extract(${table}.binding,'$[1]') AND r.subject=${table}.subject AND r.state!='Complete')`).run(current);
      this.db.exec(`DELETE FROM erdon_tiktok_refresh_claims WHERE NOT EXISTS
        (SELECT 1 FROM erdon_tiktok_refresh_credentials r WHERE r.binding=erdon_tiktok_refresh_claims.binding AND r.subject=erdon_tiktok_refresh_claims.subject)`);
      const exists = this.db.prepare('SELECT 1 FROM erdon_tiktok_credentials WHERE binding=? AND subject=?').get(scope, subject);
      if (!exists && this.db.prepare('SELECT COUNT(*) AS count FROM erdon_tiktok_credentials').get().count >= this.capacity) throw new Error();
      if (refreshToken !== null) {
        const previous = this.db.prepare('SELECT 1 FROM erdon_tiktok_refresh_credentials WHERE binding=? AND subject=?').get(scope, subject);
        if (!previous && this.db.prepare('SELECT COUNT(*) AS count FROM erdon_tiktok_refresh_credentials').get().count >= this.capacity) throw new Error();
      }
      this.write(scope, subject, accessToken, expiresAt, false);
      if (refreshToken !== null) this.write(scope, subject, refreshToken, refreshExpiresAt, true);
      else this.db.prepare('DELETE FROM erdon_tiktok_refresh_credentials WHERE binding=? AND subject=?').run(scope, subject);
      this.db.prepare('DELETE FROM erdon_tiktok_refresh_claims WHERE binding=? AND subject=?').run(scope, subject);
      return true;
    });
  }
  write(scope, subject, value, expiresAt, refresh) {
      const table = refresh ? 'erdon_tiktok_refresh_credentials' : 'erdon_tiktok_credentials';
      const nonce = randomBytes(12), keyId = this.currentKeyId;
      const cipher = createCipheriv('aes-256-gcm', this.#keys.get(keyId), nonce);
      cipher.setAAD(aad(scope, subject, expiresAt, keyId, refresh));
      const ciphertext = Buffer.concat([cipher.update(value, 'utf8'), cipher.final()]);
      this.db.prepare(`INSERT INTO ${table} VALUES (?,?,?,?,?,?,?)
        ON CONFLICT(binding,subject) DO UPDATE SET expires_at=excluded.expires_at,key_id=excluded.key_id,
        nonce=excluded.nonce,ciphertext=excluded.ciphertext,tag=excluded.tag`)
        .run(scope, subject, expiresAt, keyId, nonce, ciphertext, cipher.getAuthTag());
  }
  async get(binding, subject) {
    return this.read(binding, subject, false);
  }
  async getRefresh(binding, subject) {
    return this.read(binding, subject, true);
  }
  read(binding, subject, refresh, revocationJob = null) {
    let scope = this.scope(binding, subject); const current = this.time();
    return this.transaction(() => {
      const job = this.revocationRow(scope, subject);
      if (revocationJob !== null) { if (!job || job.jobId !== revocationJob || job.state !== 'Sending') throw new Error(); scope = job.binding; }
      else if (job && job.state !== 'Complete') return null;
      const table = refresh ? 'erdon_tiktok_refresh_credentials' : 'erdon_tiktok_credentials';
      const row = this.db.prepare(`SELECT * FROM ${table} WHERE binding=? AND subject=?`).get(scope, subject);
      if (!row || (revocationJob === null && row.expires_at <= current)) return null;
      if (!Number.isSafeInteger(row.expires_at) || row.nonce.length !== 12 || row.tag.length !== 16 || row.ciphertext.length > 8192) throw new Error();
      const decipher = createDecipheriv('aes-256-gcm', this.#keys.get(row.key_id), row.nonce);
      decipher.setAAD(aad(scope, subject, row.expires_at, row.key_id, refresh)); decipher.setAuthTag(row.tag);
      const accessToken = Buffer.concat([decipher.update(row.ciphertext), decipher.final()]).toString('utf8');
      if (!token(accessToken)) throw new Error();
      return Object.freeze(refresh ? { binding: scope, subject, refreshToken: accessToken, refreshExpiresAt: row.expires_at, revision: Buffer.from(row.nonce).toString('base64') }
        : { binding: scope, subject, accessToken, expiresAt: row.expires_at });
    });
  }
  async remove(binding, subject) {
    const scope = this.scope(binding, subject);
    this.transaction(() => {
      const job = this.revocationRow(scope, subject);
      if (job && job.state !== 'Complete') throw new Error();
      this.db.prepare('DELETE FROM erdon_tiktok_credentials WHERE binding=? AND subject=?').run(scope, subject);
      this.db.prepare('DELETE FROM erdon_tiktok_refresh_credentials WHERE binding=? AND subject=?').run(scope, subject);
      this.db.prepare('DELETE FROM erdon_tiktok_refresh_claims WHERE binding=? AND subject=?').run(scope, subject);
    });
  }
  claimRefresh(binding, subject, revision) {
    const scope = this.scope(binding, subject);
    return this.transaction(() => {
      const revoke = this.revocationRow(scope, subject);
      if (revoke && revoke.state !== 'Complete') return { state: 'Blocked' };
      const row = this.db.prepare('SELECT nonce FROM erdon_tiktok_refresh_credentials WHERE binding=? AND subject=?').get(scope, subject);
      if (!row || Buffer.from(row.nonce).toString('base64') !== revision) return { state: 'Changed' };
      const previous = this.db.prepare('SELECT state FROM erdon_tiktok_refresh_claims WHERE binding=? AND subject=?').get(scope, subject);
      if (previous) return { state: previous.state };
      const owner = randomBytes(24).toString('hex');
      this.db.prepare("INSERT INTO erdon_tiktok_refresh_claims VALUES(?,?,?,?,'Sending')").run(scope, subject, revision, owner);
      return { state: 'Acquired', owner };
    });
  }
  uncertainRefresh(binding, subject, owner) {
    const scope = this.scope(binding, subject);
    return this.transaction(() => this.db.prepare("UPDATE erdon_tiktok_refresh_claims SET state='Uncertain' WHERE binding=? AND subject=? AND owner=? AND state='Sending'")
      .run(scope, subject, owner).changes === 1);
  }
  authorizationRevision() {
    return this.transaction(() => this.db.prepare('SELECT revision FROM erdon_tiktok_authorization_revision WHERE id=1').get().revision);
  }
  revocationRow(scope, subject) {
    // OAuth authorization is Client-Key scoped, even when games/environments differ.
    const appId = JSON.parse(scope)[1];
    const pending = this.db.prepare(`SELECT COUNT(*) AS n FROM erdon_tiktok_revocations
      WHERE json_extract(binding,'$[1]')=? AND subject=? AND state!='Complete'`).get(appId,subject);
    if(pending.n>1)throw new Error('Conflicting legacy revocations');
    return this.db.prepare(`SELECT binding,job_id AS jobId,state,revision FROM erdon_tiktok_revocations
      WHERE json_extract(binding,'$[1]')=? AND subject=? ORDER BY (state!='Complete') DESC,revision DESC LIMIT 1`).get(appId, subject);
  }
  authorizationBindings(binding, subject) {
    this.scope(binding, subject);
    return this.transaction(() => this.db.prepare(`SELECT binding FROM erdon_tiktok_credentials WHERE subject=? AND json_extract(binding,'$[1]')=?
      UNION SELECT binding FROM erdon_tiktok_refresh_credentials WHERE subject=? AND json_extract(binding,'$[1]')=?
      UNION SELECT binding FROM erdon_tiktok_revocations WHERE subject=? AND json_extract(binding,'$[1]')=?`)
      .all(subject,binding.appId,subject,binding.appId,subject,binding.appId).map(row=>row.binding));
  }
  revocation(binding, subject) {
    const scope = this.scope(binding, subject);
    return this.transaction(() => {
      const row = this.revocationRow(scope, subject);
      return row ? Object.freeze({ ...row }) : null;
    });
  }
  nextAuthorizationRevision() {
    const revision = this.db.prepare('SELECT revision FROM erdon_tiktok_authorization_revision WHERE id=1').get().revision;
    if (!Number.isSafeInteger(revision) || revision >= Number.MAX_SAFE_INTEGER) throw new Error();
    this.db.prepare('UPDATE erdon_tiktok_authorization_revision SET revision=? WHERE id=1').run(revision + 1);
    return revision + 1;
  }
  beginRevocation(binding, subject) {
    const scope = this.scope(binding, subject);
    return this.transaction(() => {
      const old = this.revocationRow(scope, subject);
      if (old && old.state !== 'Complete') return { ...old };
      if (!this.db.prepare('SELECT 1 FROM erdon_tiktok_revocations WHERE binding=? AND subject=?').get(scope,subject) &&
          this.db.prepare('SELECT COUNT(*) AS count FROM erdon_tiktok_revocations').get().count >= this.capacity) throw new Error();
      if (!this.db.prepare('SELECT 1 FROM erdon_tiktok_credentials WHERE binding=? AND subject=?').get(scope, subject)) throw new Error();
      const jobId = randomBytes(24).toString('hex'), revision = this.nextAuthorizationRevision();
      this.db.prepare(`INSERT INTO erdon_tiktok_revocations VALUES(?, ?, ?, 'Pending', ?)
        ON CONFLICT(binding,subject) DO UPDATE SET job_id=excluded.job_id,state=excluded.state,revision=excluded.revision`).run(scope, subject, jobId, revision);
      return { jobId, state: 'Pending', revision };
    });
  }
  transitionRevocation(binding, subject, jobId, from, to) {
    const scope = this.scope(binding, subject);
    if (!['Pending:Sending','Sending:Uncertain','Sending:Confirmed','Uncertain:Pending','Sending:Pending','Confirmed:Finalizing','Finalizing:Confirmed'].includes(from + ':' + to)) throw new TypeError('Invalid revocation transition');
    return this.transaction(() => this.db.prepare("UPDATE erdon_tiktok_revocations SET state=? WHERE json_extract(binding,'$[1]')=? AND subject=? AND job_id=? AND state=?")
      .run(to, binding.appId, subject, jobId, from).changes === 1);
  }
  completeRevocation(binding, subject, jobId) {
    const scope = this.scope(binding, subject);
    this.transaction(() => {
      const row = this.revocationRow(scope, subject);
      if (!row || row.jobId !== jobId) throw new Error();
      if (row.state === 'Complete') return;
      if (row.state !== 'Finalizing') throw new Error();
      for (const table of ['erdon_tiktok_credentials','erdon_tiktok_refresh_credentials','erdon_tiktok_refresh_claims'])
        this.db.prepare(`DELETE FROM ${table} WHERE json_extract(binding,'$[1]')=? AND subject=?`).run(binding.appId, subject);
      this.db.prepare("UPDATE erdon_tiktok_revocations SET state='Complete',revision=? WHERE binding=? AND subject=?")
        .run(this.nextAuthorizationRevision(), row.binding, subject);
    });
  }
  confirmRevocationEvidence(binding, subject, jobId, evidence) {
    const scope=this.scope(binding,subject);
    return this.transaction(()=>{
      const job=this.revocationRow(scope,subject);
      if(!job || job.jobId!==jobId)throw new Error();
      const old=this.db.prepare('SELECT envelope_hash FROM erdon_tiktok_revocation_evidence WHERE job_id=?').get(jobId);
      if(old){if(old.envelope_hash!==evidence.envelopeHash)throw new Error();return;}
      if(!['Sending','Uncertain'].includes(job.state))throw new Error();
      if(this.db.prepare('SELECT COUNT(*) AS n FROM erdon_tiktok_revocation_evidence').get().n>=this.capacity)throw new Error();
      this.db.prepare('INSERT INTO erdon_tiktok_revocation_evidence VALUES(?,?,?,?,?,?,?)').run(jobId,evidence.evidenceId,
        evidence.envelopeHash,evidence.reviewer,evidence.sourceReference,evidence.sourceSha256,this.time());
      this.db.prepare("UPDATE erdon_tiktok_revocations SET state='Confirmed' WHERE binding=? AND subject=? AND job_id=?")
        .run(job.binding,subject,jobId);
    });
  }
  close() {
    if (this.closed) return;
    this.closed = true;
    try { this.db.close(); } finally { for (const key of this.#keys.values()) key.fill(0); this.#keys.clear(); }
  }
}
