import { createServer } from 'node:http';
import { AuthService, MemoryAuthStore } from './auth.mjs';
import { createWeChatVerifier } from './wechat.mjs';
import { createAuthHandler } from './http.mjs';
import { SqliteAuthStore } from './sqlite-store.mjs';

// Deliberately requires a local-only opt-in; memory sessions are not a production deployment.
if (process.env.SDK_LOCAL_DEVELOPMENT !== '1') throw new Error('Set SDK_LOCAL_DEVELOPMENT=1 for the loopback reference server');
const binding = {
  gameId: process.env.SDK_GAME_ID, appId: process.env.WECHAT_APP_ID,
  environment: 'test', platform: 'WeChat', secret: process.env.WECHAT_APP_SECRET
};
const store = process.env.SDK_AUTH_DB ? new SqliteAuthStore(process.env.SDK_AUTH_DB) : new MemoryAuthStore();
const service = new AuthService({ bindings: [binding], verifiers: { WeChat: createWeChatVerifier() }, store });
const server = createServer({ requestTimeout: 10000, headersTimeout: 5000, maxHeaderSize: 8192 }, createAuthHandler(service));
server.listen(8787, '127.0.0.1', () => console.info('SDK reference auth listening on loopback port 8787; use HTTPS termination for Unity integration'));
for (const signal of ['SIGINT', 'SIGTERM']) process.once(signal, () => {
  server.close(() => { store.close?.(); process.exit(0); });
});
