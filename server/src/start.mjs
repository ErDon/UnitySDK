import { createServer } from 'node:http';
import { AuthService, MemoryAuthStore } from './auth.mjs';
import { createWeChatVerifier } from './wechat.mjs';
import { createAuthHandler } from './http.mjs';

// Deliberately requires a local-only opt-in; memory sessions are not a production deployment.
if (process.env.SDK_LOCAL_DEVELOPMENT !== '1') throw new Error('Set SDK_LOCAL_DEVELOPMENT=1 for the loopback reference server');
const binding = {
  gameId: process.env.SDK_GAME_ID, appId: process.env.WECHAT_APP_ID,
  environment: 'test', platform: 'WeChat', secret: process.env.WECHAT_APP_SECRET
};
const service = new AuthService({ bindings: [binding], verifiers: { WeChat: createWeChatVerifier() }, store: new MemoryAuthStore() });
const server = createServer({ requestTimeout: 10000, headersTimeout: 5000, maxHeaderSize: 8192 }, createAuthHandler(service));
server.listen(8787, '127.0.0.1', () => console.info('SDK reference auth listening on loopback port 8787; use HTTPS termination for Unity integration'));
