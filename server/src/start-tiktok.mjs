import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { startTikTokServer } from './tiktok-server.mjs';

// A trusted deployment module exports default configuration, including the game's deliver callback.
// Do not point this variable at an uploaded file or other untrusted path.
try {
  const path = process.env.SDK_TIKTOK_CONFIG;
  const rawPort = process.env.SDK_PORT ?? '8787';
  if (!path || !/^\d{1,5}$/.test(rawPort)) throw new Error();
  const configuration = (await import(pathToFileURL(resolve(path)).href)).default;
  const running = await startTikTokServer(configuration, { port: Number(rawPort) });
  console.info(`TikTok SDK server listening on 127.0.0.1:${running.port}; configure HTTPS termination`);
  for (const signal of ['SIGINT', 'SIGTERM']) process.once(signal, () => {
    running.stop().then(() => { process.exitCode = 0; }, () => { console.error('SDK server shutdown failed'); process.exitCode = 1; });
  });
} catch {
  // Import/configuration errors may contain secret values; report only a fixed diagnostic.
  console.error('SDK server startup failed; check deployment configuration, storage, and port');
  process.exitCode = 1;
}
