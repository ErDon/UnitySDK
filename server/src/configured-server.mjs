import { createServer } from 'node:http';
import { mkdir } from 'node:fs/promises';
import { dirname } from 'node:path';
import { AuthService } from './auth.mjs';
import { SqliteAuthStore } from './sqlite-store.mjs';
import { createAuthHandler } from './http.mjs';
import { createWeChatVerifier } from './wechat.mjs';
import { createDouyinVerifier } from './douyin.mjs';
import { createTikTokVerifier } from './tiktok.mjs';
import { createTikTokApplication } from './tiktok-application.mjs';
import { createDouyinApplication } from './douyin-application.mjs';
import { DeploymentConfigError } from './deployment-config.mjs';

function loginApplication(config, options) {
  const store = new SqliteAuthStore(config.authPath, { now: options.now });
  try {
    const verifiers = { WeChat: createWeChatVerifier, Douyin: createDouyinVerifier, TikTok: createTikTokVerifier };
    const auth = new AuthService({ bindings: [config.binding], store, now: options.now,
      verifiers: { [config.binding.platform]: verifiers[config.binding.platform]({ fetchImpl: options.fetchImpl }) } });
    return { handler: createAuthHandler(auth, { log: options.log }), close: () => store.close() };
  } catch (error) { store.close(); throw error; }
}

// Config was resolved before this entry point. No outbound calls occur during startup.
export async function startConfiguredServer(resolved, { fetchImpl = fetch, now = Date.now, log = console.info, deliver = null } = {}) {
  if (!resolved?.preview?.ready || !resolved.configuration) throw new DeploymentConfigError('environment', 'CONFIG.ENV_MISSING');
  if (typeof fetchImpl !== 'function' || typeof now !== 'function' || typeof log !== 'function' ||
      (deliver !== null && typeof deliver !== 'function')) throw new DeploymentConfigError('runtime');
  const config = resolved.configuration, options = { fetchImpl, now, log, deliver };
  for (const path of [config.authPath, config.orderPath, config.credentialPath].filter(Boolean)) await mkdir(dirname(path), { recursive: true });
  // 支付关闭只创建登录服务，不加载订单库和支付凭据库；微信支付由配置解析阶段拒绝。
  const app = resolved.paymentsEnabled
    ? (config.binding.platform === 'TikTok' ? createTikTokApplication : createDouyinApplication)({ ...config, ...options })
    : loginApplication(config, options);
  const server = createServer({ requestTimeout: 10000, headersTimeout: 5000, maxHeaderSize: 8192 }, app.handler);
  try {
    await new Promise((accept, reject) => {
      // 仅监听回环地址，公网 HTTPS 及平台回调入口应由部署层反向代理提供。
      server.once('error', reject); server.listen(resolved.port, '127.0.0.1', () => { server.removeListener('error', reject); accept(); });
    });
  } catch { await app.close(); throw new DeploymentConfigError('listener', 'CONFIG.LISTEN_FAILED'); }
  let stopping;
  return Object.freeze({ port: server.address().port, stop() {
    // 先等待 HTTP 请求结束，再关闭工作器/数据库；重复 stop 复用同一个关闭任务。
    if (!stopping) stopping = new Promise(accept => server.close(accept)).then(() => app.close());
    return stopping;
  } });
}
