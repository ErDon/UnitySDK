import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { loadDeploymentConfig, DeploymentConfigError } from './deployment-config.mjs';
import { startConfiguredServer } from './configured-server.mjs';

try {
  const check = process.argv[2] === 'check';
  if (!['check', 'start'].includes(process.argv[2]) || process.argv.length > 4) throw new DeploymentConfigError('arguments');
  const file = process.argv[3] ?? process.env.SDK_SERVER_CONFIG;
  if (!file) throw new DeploymentConfigError('SDK_SERVER_CONFIG', 'CONFIG.FILE_REQUIRED');
  const resolved = await loadDeploymentConfig(file, { requireSecrets: false });
  // Only the explicit preview is printed. Never stringify resolved.configuration.
  console.info(JSON.stringify(resolved.preview));
  if (!resolved.preview.ready) {
    process.exitCode = 1;
  } else if (!check) {
    let deliver = null;
    if (process.env.SDK_DELIVERY_MODULE) {
      try {
        deliver = (await import(pathToFileURL(resolve(process.env.SDK_DELIVERY_MODULE)).href)).deliver;
        if (typeof deliver !== 'function') throw new Error();
      } catch { throw new DeploymentConfigError('SDK_DELIVERY_MODULE', 'CONFIG.DELIVERY_MODULE_INVALID'); }
    }
    const running = await startConfiguredServer(resolved, { deliver });
    console.info(`SDK server listening on 127.0.0.1:${running.port}; HTTPS proxy required for clients`);
    if (resolved.paymentsEnabled && !deliver) console.info('Delivery module absent: verified orders remain Paid until a delivery worker is configured');
    for (const signal of ['SIGINT', 'SIGTERM']) process.once(signal, () => {
      running.stop().then(() => { process.exitCode = 0; }, () => { console.error('SERVER.SHUTDOWN_FAILED'); process.exitCode = 1; });
    });
  }
} catch (error) {
  console.error(error instanceof DeploymentConfigError ? error.message : 'SERVER.STARTUP_FAILED');
  process.exitCode = 1;
}
