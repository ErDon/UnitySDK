import { AuthError, bindingKey, digest } from './auth.mjs';

// Server-owned orchestration. Durable states block use while the remote result is uncertain.
// Recovery that resets Sending/Finalizing requires the previous worker to be stopped.
export class TikTokRevocation {
  constructor({ binding, auth, credentials, managedCredentials, client, verifyEvidence = null } = {}) {
    if (binding?.platform !== 'TikTok' || typeof auth?.store?.revokeSubject !== 'function' || typeof auth?.store?.bindingsForApplication !== 'function' ||
        typeof credentials?.beginRevocation !== 'function' || typeof managedCredentials?.get !== 'function' || typeof client?.revoke !== 'function')
      throw new TypeError('Invalid TikTok revocation configuration');
    this.binding = Object.freeze({ ...binding }); this.scope = bindingKey(binding);
    this.auth = auth; this.credentials = credentials; this.managed = managedCredentials; this.client = client; this.running = new Map();
    if(verifyEvidence!==null && typeof verifyEvidence!=='function')throw new TypeError('Invalid evidence verifier');
    this.verifyEvidence=verifyEvidence;
  }
  assertAuthorized(binding, subject) {
    try {
      const job = this.credentials.revocation(binding, subject);
      if (job && job.state !== 'Complete') throw new AuthError('AUTH.REVOCATION_PENDING', 409);
    } catch (error) {
      if (error instanceof AuthError) throw error;
      throw new AuthError('AUTH.CREDENTIAL_STORAGE_FAILED', 503);
    }
  }
  async revoke(session) {
    if (typeof session !== 'string' || !/^[A-Za-z0-9_-]{43}$/.test(session)) throw new AuthError('AUTH.SESSION_INVALID');
    // Only this recovery entry bypasses the pending-authorization policy. It still checks
    // the stored bearer session, expiry and fixed application binding; no subject comes from HTTP.
    const record = await this.auth.store.getSession(digest(session));
    if (!record || record.binding !== this.scope) throw new AuthError('AUTH.SESSION_INVALID');
    return this.run(record.subject, false);
  }
  resume(subject, { previousWorkerStopped = false } = {}) {
    // Trusted server API, intentionally not an HTTP route.
    return this.run(subject, true, previousWorkerStopped);
  }
  async resolve(subject, { jobId, evidence, previousWorkerStopped = false } = {}) {
    if(!this.verifyEvidence)throw new AuthError('RECOVERY.NOT_CONFIGURED',503);
    if(previousWorkerStopped!==true || this.running.has(subject))throw new AuthError('AUTH.REVOCATION_PENDING',409);
    let checked;
    try { checked=await this.verifyEvidence(evidence); }
    catch { throw new AuthError('RECOVERY.EVIDENCE_INVALID',400); }
    if(!checked || checked.kind!=='authorization-revoked' || checked.platform!=='TikTok' || checked.appId!==this.binding.appId ||
        checked.subject!==subject || checked.jobId!==jobId || checked.gameId!==this.binding.gameId || checked.environment!==this.binding.environment)
      throw new AuthError('RECOVERY.EVIDENCE_MISMATCH',409);
    if(this.running.has(subject))throw new AuthError('AUTH.REVOCATION_PENDING',409);
    this.credentials.confirmRevocationEvidence(this.binding,subject,jobId,checked);
    return this.resume(subject,{previousWorkerStopped:true});
  }
  run(subject, recovery, previousWorkerStopped = false) {
    if (this.running.has(subject)) return this.running.get(subject);
    const operation = this.execute(subject, recovery, previousWorkerStopped).catch(error => {
      if (error instanceof AuthError) throw error;
      throw new AuthError('AUTH.REVOCATION_STORAGE_FAILED', 503);
    }).finally(() => this.running.delete(subject));
    this.running.set(subject, operation); return operation;
  }
  async execute(subject, recovery, previousWorkerStopped) {
    const store = this.credentials, binding = this.binding;
    let job = store.revocation(binding, subject);
    if (recovery && !job) throw new AuthError('AUTH.REVOCATION_NOT_FOUND', 404);
    if (recovery && job.state === 'Complete') return { revoked: true, scope: 'tiktok-authorization' };
    if (!job || job.state === 'Complete') {
      let token;
      try { token = await this.managed.get(binding, subject); }
      catch { throw new AuthError('AUTH.CREDENTIAL_UNAVAILABLE', 503); }
      if (!token) throw new AuthError('AUTH.CREDENTIAL_UNAVAILABLE', 503);
      job = store.beginRevocation(binding, subject);
    }
    if (['Sending', 'Uncertain', 'Finalizing'].includes(job.state)) {
      if (!recovery || previousWorkerStopped !== true) throw new AuthError('AUTH.REVOCATION_UNCERTAIN', 503);
      const next = job.state === 'Finalizing' ? 'Confirmed' : 'Pending';
      if (!store.transitionRevocation(binding, subject, job.jobId, job.state, next)) throw new AuthError('AUTH.REVOCATION_PENDING', 409);
      job = store.revocation(binding, subject);
    }
    if (job.state === 'Pending') {
      if (!store.transitionRevocation(binding, subject, job.jobId, 'Pending', 'Sending')) throw new AuthError('AUTH.REVOCATION_PENDING', 409);
      try {
        const token = store.read(binding, subject, false, job.jobId);
        if (!token) throw new Error();
        await this.client.revoke(binding, token.accessToken);
      } catch {
        store.transitionRevocation(binding, subject, job.jobId, 'Sending', 'Uncertain');
        throw new AuthError('AUTH.REVOCATION_UNCERTAIN', 503);
      }
      if (!store.transitionRevocation(binding, subject, job.jobId, 'Sending', 'Confirmed')) throw new AuthError('AUTH.REVOCATION_PENDING', 409);
    }
    if (!store.transitionRevocation(binding, subject, job.jobId, 'Confirmed', 'Finalizing')) throw new AuthError('AUTH.REVOCATION_PENDING', 409);
    // Keep the barrier active until both databases have committed. A crash can resume
    // local cleanup without sending another OAuth request when platform success is durable.
    const scopes = new Set([this.scope, ...store.authorizationBindings(binding, subject),
      ...await this.auth.store.bindingsForApplication(binding.appId, 'TikTok', subject)]);
    for (const scope of scopes) await this.auth.store.revokeSubject(scope, subject);
    store.completeRevocation(binding, subject, job.jobId);
    return Object.freeze({ revoked: true, scope: 'tiktok-authorization' });
  }
}
