import { createServer } from 'node:http';
import { AuthService, MemoryAuthStore } from './auth.mjs';
import { createWeChatVerifier } from './wechat.mjs';
import { createDouyinVerifier } from './douyin.mjs';
import { createTikTokVerifier } from './tiktok.mjs';
import { createAuthHandler } from './http.mjs';
import { SqliteAuthStore } from './sqlite-store.mjs';

// Deliberately requires a local-only opt-in; memory sessions are not a production deployment.
if (process.env.SDK_LOCAL_DEVELOPMENT !== '1') throw new Error('Set SDK_LOCAL_DEVELOPMENT=1 for the loopback reference server');
const bindings = [];
for (const [platform, prefix] of [['WeChat', 'WECHAT'], ['Douyin', 'DOUYIN'], ['TikTok', 'TIKTOK']]) {
  const appId = process.env[`${prefix}_APP_ID`], secret = process.env[`${prefix}_APP_SECRET`];
  if (appId || secret) bindings.push({ gameId: process.env.SDK_GAME_ID, appId, secret, environment: 'test', platform });
}
if (bindings.length === 0) throw new Error('Configure a WeChat, Douyin or TikTok application binding');
const store = process.env.SDK_AUTH_DB ? new SqliteAuthStore(process.env.SDK_AUTH_DB) : new MemoryAuthStore();
const service = new AuthService({ bindings, verifiers: { WeChat: createWeChatVerifier(), Douyin: createDouyinVerifier(), TikTok: createTikTokVerifier() }, store });
const server = createServer({ requestTimeout: 10000, headersTimeout: 5000, maxHeaderSize: 8192 }, createAuthHandler(service));
server.listen(8787, '127.0.0.1', () => console.info('SDK reference auth listening on loopback port 8787; use HTTPS termination for Unity integration'));
for (const signal of ['SIGINT', 'SIGTERM']) process.once(signal, () => {
  server.close(() => { store.close?.(); process.exit(0); });
});
