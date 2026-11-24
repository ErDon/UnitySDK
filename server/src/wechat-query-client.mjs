import { OrderError } from './orders.mjs';
import { wechatHmac } from './wechat-payments.mjs';

// Stable token ordinary mode does not invalidate another process's token.
export function createWeChatQueryClient({ binding, appKey, credentialStore, fetchImpl = fetch, now = Date.now, timeoutMs = 8000 } = {}) {
  let cached, pending;
  async function request(url, body) {
    let reader;
    try {
      const response = await fetchImpl(url, { method: 'POST', headers: { 'Content-Type': 'application/json' },
        body, redirect: 'error', signal: AbortSignal.timeout(timeoutMs) });
      if (!response.ok) { await response.body?.cancel(); throw new Error(); }
      reader = response.body.getReader(); const chunks = []; let size = 0;
      for (;;) { const { value, done } = await reader.read(); if (done) break;
        size += value.length; if (size > 16384) throw new Error(); chunks.push(value); }
      const data = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(Buffer.concat(chunks)));
      if (!data || typeof data !== 'object' || Array.isArray(data)) throw new Error();
      return data;
    } catch { throw new OrderError('PAY.QUERY_UNAVAILABLE', 502); }
    finally { try { await reader?.cancel(); } catch {} }
  }
  async function accessToken() {
    const time = now();
    if (cached && time >= cached.created && time < cached.expires) return cached.token;
    if (pending) return pending;
    pending = (async () => {
      const data = await request('https://api.weixin.qq.com/cgi-bin/stable_token', JSON.stringify({ grant_type: 'client_credential', appid: binding.appId, secret: binding.secret, force_refresh: false }));
      if ((data.errcode !== undefined && data.errcode !== 0) || typeof data.access_token !== 'string' || !/^[\x21-\x7e]{1,8192}$/.test(data.access_token) ||
          !Number.isSafeInteger(data.expires_in) || data.expires_in <= 0 || data.expires_in > 86400) throw new OrderError('PAY.CREDENTIAL_RESPONSE_INVALID', 502);
      cached = { token: data.access_token, created: time, expires: time + Math.max(0, Math.min(3600, data.expires_in - 60)) * 1000 };
      return cached.token;
    })();
    try { return await pending; } finally { pending = null; }
  }
  return Object.freeze({ async query(subject, preparation) {
    const credential = await credentialStore.get(binding, subject);
    if (!credential) throw new OrderError('PAY.SESSION_EXPIRED', 401);
    const token = await accessToken(), path = '/wxa/game/queryorderinfo';
    const body = JSON.stringify({ openid: subject, offer_id: preparation.offerId, ts: Math.floor(now() / 1000),
      zone_id: preparation.zoneId, env: preparation.env, out_trade_no: preparation.outTradeNo, biz_id: 2 });
    const url = new URL('https://api.weixin.qq.com' + path);
    url.search = new URLSearchParams({ access_token: token, sig_method: 'hmac_sha256',
      signature: wechatHmac(credential.sessionKey, body), pay_sig: wechatHmac(appKey, path + '&' + body) });
    const data = await request(url, body);
    if (data.errcode === 90016) throw new OrderError('PAY.SESSION_EXPIRED', 401);
    if (data.errcode === 90019) return { status: 'not_found' };
    if (data.errcode !== 0 || data.out_trade_no !== preparation.outTradeNo || data.product_id !== preparation.productId ||
        ![1, 2].includes(data.pay_state) || ![1, 2].includes(data.deliver_state)) throw new OrderError('PAY.QUERY_RESPONSE_INVALID', 502);
    // Query omits amount and payer evidence; retain it as a diagnostic, never a grant.
    return { status: data.pay_state === 2 ? 'paid' : 'unpaid', delivered: data.deliver_state === 2 };
  } });
}
