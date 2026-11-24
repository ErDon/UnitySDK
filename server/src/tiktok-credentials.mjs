import { bindingKey } from './auth.mjs';
import { OrderError } from './orders.mjs';

// One service instance coalesces refreshes; durable compare-and-swap rejects stale
// writes after a new login or removal, including across service processes.
export class TikTokCredentials {
  constructor({ binding, store, client, now = Date.now, refreshBeforeMs = 60000 } = {}) {
    if (binding?.platform !== 'TikTok' || typeof store?.get !== 'function' || typeof store?.getRefresh !== 'function' ||
        typeof store?.put !== 'function' || typeof client?.refresh !== 'function' || typeof now !== 'function' ||
        !Number.isSafeInteger(refreshBeforeMs) || refreshBeforeMs < 0 || refreshBeforeMs > 3600000)
      throw new TypeError('Invalid TikTok credential manager');
    this.binding=Object.freeze({...binding});this.scope=bindingKey(binding);this.store=store;this.client=client;
    this.now=now;this.refreshBeforeMs=refreshBeforeMs;this.running=new Map();
  }
  get(binding, subject) {
    if (bindingKey(binding)!==this.scope) return Promise.reject(new OrderError('PAY.CREDENTIAL_UNAVAILABLE',503));
    // 仅合并当前服务实例的刷新；这不是跨进程锁，不能阻止多个进程同时请求上游。
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
      const next=await this.client.refresh(this.binding,subject,refresh.refreshToken);
      if (!next||next.subject!==subject) throw new Error();
      // revision 是读取刷新令牌时的版本：并发新登录/删除后不得用旧结果覆盖新状态。
      await this.store.put(this.binding,subject,next,refresh.revision);
      // A concurrent login/removal wins; never return a refresh result that failed CAS.
      return await this.store.get(this.binding,subject);
    } catch { throw new OrderError('PAY.CREDENTIAL_UNAVAILABLE',503); }
  }
}
