import { bindingKey } from './auth.mjs';
import { OrderError } from './orders.mjs';

// One service instance coalesces refreshes; durable compare-and-swap rejects stale
// writes after a new login or removal, including across service processes.
export class TikTokCredentials {
  constructor({ binding, store, client, now = Date.now, refreshBeforeMs = 60000 } = {}) {
    if (binding?.platform !== 'TikTok' || typeof store?.get !== 'function' || typeof store?.getRefresh !== 'function' ||
        typeof store?.put !== 'function' || typeof store?.claimRefresh !== 'function' || typeof store?.uncertainRefresh !== 'function' || typeof client?.refresh !== 'function' || typeof now !== 'function' ||
        !Number.isSafeInteger(refreshBeforeMs) || refreshBeforeMs < 0 || refreshBeforeMs > 3600000)
      throw new TypeError('Invalid TikTok credential manager');
    this.binding=Object.freeze({...binding});this.scope=bindingKey(binding);this.store=store;this.client=client;
    this.now=now;this.refreshBeforeMs=refreshBeforeMs;this.running=new Map();
  }
  get(binding, subject) {
    if (bindingKey(binding)!==this.scope) return Promise.reject(new OrderError('PAY.CREDENTIAL_UNAVAILABLE',503));
    // 同实例合并 Promise；下方 SQLite 原子领取保护跨进程的单次平台请求。
    if (this.running.has(subject)) return this.running.get(subject);
    const operation=this.load(subject).finally(()=>this.running.delete(subject));
    this.running.set(subject,operation);return operation;
  }
  async load(subject) {
    try {
      const current=await this.store.get(this.binding,subject), now=this.now();
      if (!Number.isSafeInteger(now)||now<0) throw new Error();
      if (current && current.expiresAt-now>this.refreshBeforeMs) return current;
      const refresh=await this.store.getRefresh(this.binding,subject);
      if (!refresh) return current;
      const claim=this.store.claimRefresh(this.binding,subject,refresh.revision);
      if(claim.state==='Changed')return await this.store.get(this.binding,subject);
      if(claim.state!=='Acquired')throw new OrderError(claim.state==='Uncertain'?'PAY.CREDENTIAL_REFRESH_UNCERTAIN':'PAY.CREDENTIAL_REFRESH_PENDING',503);
      try {
        const next=await this.client.refresh(this.binding,subject,refresh.refreshToken);
        if (!next||next.subject!==subject) throw new Error();
        // owner + revision prevent both a second worker and a stale login result from committing.
        await this.store.put(this.binding,subject,next,refresh.revision,0,claim.owner);
      } catch {
        this.store.uncertainRefresh(this.binding,subject,claim.owner);
        throw new OrderError('PAY.CREDENTIAL_REFRESH_UNCERTAIN',503);
      }
      // A concurrent login/removal wins; never return a refresh result that failed CAS.
      return await this.store.get(this.binding,subject);
    } catch(error) {
      if(error instanceof OrderError && ['PAY.CREDENTIAL_REFRESH_PENDING','PAY.CREDENTIAL_REFRESH_UNCERTAIN'].includes(error.code))throw error;
      throw new OrderError('PAY.CREDENTIAL_UNAVAILABLE',503);
    }
  }
}
