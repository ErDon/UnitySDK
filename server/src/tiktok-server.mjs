import { createServer } from 'node:http';
import { createTikTokApplication } from './tiktok-application.mjs';

// Loopback listener for an HTTPS reverse proxy. Configuration contains server-only secrets.
export async function startTikTokServer(configuration, { port = 8787 } = {}) {
  if (!Number.isInteger(port) || port < 0 || port > 65535) throw new TypeError('Invalid server port');
  const app = createTikTokApplication(configuration);
  const server = createServer({ requestTimeout: 10000, headersTimeout: 5000, maxHeaderSize: 8192 }, app.handler);
  try {
    await new Promise((resolve, reject) => {
      server.once('error', reject);
      server.listen(port, '127.0.0.1', () => { server.removeListener('error', reject); resolve(); });
    });
  } catch {
    await app.close(); throw new Error('TikTok server could not start');
  }
  let stopping;
  const stop = () => {
    if (!stopping) stopping = new Promise(resolve => server.close(resolve)).then(() => app.close());
    return stopping;
  };
  return Object.freeze({ port: server.address().port, stop });
}
