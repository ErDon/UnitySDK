import { open } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { validateTikTokTradeRequest } from './tiktok-trade-client.mjs';

export class DeploymentConfigError extends Error {
  constructor(field, code = 'CONFIG.INVALID') { super(`${code}: ${field}`); this.code = code; this.field = field; }
}
const reject = (field, code) => { throw new DeploymentConfigError(field, code); };
const text = (value, max = 256) => typeof value === 'string' && value.length > 0 && value.length <= max &&
  value.trim() === value && !/[\u0000-\u001f\u007f]/.test(value);
function object(value, allowed, field) {
  if (!value || typeof value !== 'object' || Array.isArray(value) || Object.keys(value).some(key => !allowed.includes(key))) reject(field);
}

// Pure inspection: no filesystem writes, database connections, network or secret printing.
// 纯配置解析：不打开数据库、不监听端口、不向平台发请求。
// requireSecrets=false 仅用于缺密钥预览；启动入口仍检查 preview.ready。
export function resolveDeploymentConfig(raw, { env = process.env, baseDir = '.', requireSecrets = true } = {}) {
  object(raw, ['schemaVersion', 'platform', 'gameId', 'environment', 'appIdEnv', 'secretEnv', 'port', 'storage', 'payments'], 'root');
  if (raw.schemaVersion !== 1) reject('schemaVersion');
  if (!['WeChat', 'Douyin', 'TikTok'].includes(raw.platform)) reject('platform');
  if (!text(raw.gameId)) reject('gameId');
  if (!['test', 'production'].includes(raw.environment)) reject('environment');
  const port = raw.port ?? 8787;
  if (!Number.isInteger(port) || port < 0 || port > 65535) reject('port');
  object(raw.storage, ['auth', 'orders', 'credentials'], 'storage');
  object(raw.payments, ['enabled', 'catalog', 'route', 'zoneId', 'callbackTokenEnv', 'isSandbox', 'keysEnv', 'currentKeyId', 'orderUrlPrefix', 'offerId', 'appKeyEnv', 'originalId'], 'payments');
  if (typeof raw.payments.enabled !== 'boolean') reject('payments.enabled');
  const required = new Set(), missing = new Set();
  const variable = (name, field, validate = value => text(value, 8192)) => {
    if (typeof name !== 'string' || !/^[A-Z][A-Z0-9_]{0,127}$/.test(name)) reject(field);
    required.add(name);
    const value = Object.hasOwn(env, name) ? env[name] : undefined;
    if (value === undefined || value === '') { missing.add(name); return undefined; }
    if (!validate(value)) reject(field, 'CONFIG.ENV_INVALID');
    return value;
  };
  const binding = { gameId: raw.gameId, environment: raw.environment, platform: raw.platform,
    appId: variable(raw.appIdEnv, 'appIdEnv', value => text(value)), secret: variable(raw.secretEnv, 'secretEnv') };
  const path = (key, needed) => {
    if (!needed && raw.storage[key] === undefined) return undefined;
    if (!text(raw.storage[key], 4096) || raw.storage[key] === ':memory:') reject('storage.' + key);
    return resolve(baseDir, raw.storage[key]);
  };
  const config = { binding, authPath: path('auth', true) };
  if (raw.payments.enabled) {
    config.orderPath = path('orders', true);
    const catalog = raw.payments.catalog;
    if (!Array.isArray(catalog) || !catalog.length || catalog.length > 10000) reject('payments.catalog');
    const ids = new Set();
    config.catalog = catalog.map(item => {
      const fields = ['productId', 'productName', 'amountMinor', 'currency'];
      if (raw.platform === 'TikTok') fields.push('tokenAmount', 'quantity', 'quantityUnit', 'imageUrl');
      object(item, fields, 'payments.catalog');
      if (!text(item.productId) || ids.has(item.productId) || !Number.isSafeInteger(item.amountMinor) || item.amountMinor <= 0 ||
          typeof item.currency !== 'string' || !/^[A-Z]{3}$/.test(item.currency)) reject('payments.catalog');
      ids.add(item.productId); return Object.freeze({ ...item });
    });
    if (raw.platform !== 'WeChat' && ['offerId', 'appKeyEnv', 'originalId'].some(key => raw.payments[key] !== undefined)) reject('payments');
    if (raw.platform === 'Douyin') {
      if (!['Android', 'IosCustomerService'].includes(raw.payments.route) || !text(raw.payments.zoneId)) reject('payments.route/zoneId');
      config.route = raw.payments.route; config.zoneId = raw.payments.zoneId;
      config.callbackToken = variable(raw.payments.callbackTokenEnv, 'payments.callbackTokenEnv', value => text(value));
      if (config.catalog.some(item => item.currency !== 'CNY' || item.amountMinor > 2147483647 || !text(item.productName, 10))) reject('payments.catalog');
      if (['isSandbox', 'keysEnv', 'currentKeyId', 'orderUrlPrefix'].some(key => raw.payments[key] !== undefined)) reject('payments');
    } else {
      if (typeof raw.payments.isSandbox !== 'boolean') reject('payments.isSandbox');
      config.isSandbox = raw.payments.isSandbox;
      config.credentialPath = path('credentials', true);
      const names = raw.payments.keysEnv;
      if (!names || typeof names !== 'object' || Array.isArray(names) || !Object.keys(names).length ||
          Object.keys(names).some(key => !/^[a-zA-Z0-9_-]{1,64}$/.test(key)) || !Object.hasOwn(names, raw.payments.currentKeyId)) reject('payments.keysEnv/currentKeyId');
      config.keys = new Map(); config.currentKeyId = raw.payments.currentKeyId;
      for (const [id, name] of Object.entries(names)) {
        const value = variable(name, 'payments.keysEnv', value => typeof value === 'string' && /^[0-9a-fA-F]{64}$/.test(value));
        if (value) config.keys.set(id, Buffer.from(value, 'hex'));
      }
      if (raw.platform === 'WeChat') {
        if (![raw.payments.offerId, raw.payments.zoneId, raw.payments.originalId].every(value => text(value))) reject('payments.offerId/zoneId/originalId');
        config.offerId = raw.payments.offerId; config.zoneId = raw.payments.zoneId; config.originalId = raw.payments.originalId;
        config.appKey = variable(raw.payments.appKeyEnv, 'payments.appKeyEnv', value => text(value));
        config.callbackToken = variable(raw.payments.callbackTokenEnv, 'payments.callbackTokenEnv', value => text(value));
        if (config.catalog.some(item => item.currency !== 'CNY' || item.amountMinor > 2147483647)) reject('payments.catalog');
        if (['route', 'orderUrlPrefix'].some(key => raw.payments[key] !== undefined)) reject('payments');
      } else {
      config.orderUrlPrefix = raw.payments.orderUrlPrefix ?? '/orders/';
      if (typeof config.orderUrlPrefix !== 'string' || !config.orderUrlPrefix.startsWith('/') || config.orderUrlPrefix.startsWith('//') ||
          config.orderUrlPrefix.length > 1024 || /[\s?#]/.test(config.orderUrlPrefix)) reject('payments.orderUrlPrefix');
      try { for (const item of config.catalog) validateTikTokTradeRequest({ ...item, orderId: 'config-check', accessToken: 'config-check', orderUrl: config.orderUrlPrefix }); }
      catch { reject('payments.catalog'); }
      if (['route', 'zoneId', 'callbackTokenEnv'].some(key => raw.payments[key] !== undefined)) reject('payments');
      }
    }
  } else if (Object.keys(raw.payments).some(key => key !== 'enabled')) reject('payments');
  const paths = [config.authPath, config.orderPath, config.credentialPath].filter(Boolean);
  if (new Set(paths.map(value => process.platform === 'win32' ? value.toLowerCase() : value)).size !== paths.length) reject('storage', 'CONFIG.STORAGE_COLLISION');
  if (requireSecrets && missing.size) reject('environment', 'CONFIG.ENV_MISSING');
  // configuration 含真实凭据，仅供服务内部装配；对外诊断只能输出下面的白名单 preview。
  return { configuration: config, paymentsEnabled: raw.payments.enabled, port,
    preview: Object.freeze({ platform: raw.platform, environment: raw.environment, paymentsEnabled: raw.payments.enabled,
      port, productCount: config.catalog?.length ?? 0, requiredVariables: [...required], missingVariables: [...missing],
      ready: missing.size === 0 }) };
}

export async function loadDeploymentConfig(file, options = {}) {
  let raw;
  try {
    const handle = await open(file, 'r');
    try {
      const buffer = Buffer.alloc(131073);
      let length = 0;
      while (length < buffer.length) {
        const { bytesRead } = await handle.read(buffer, length, buffer.length - length, null);
        if (!bytesRead) break; length += bytesRead;
      }
      if (length > 131072) reject('file', 'CONFIG.TOO_LARGE');
      raw = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(buffer.subarray(0, length)));
    } finally { await handle.close(); }
  } catch (error) {
    if (error instanceof DeploymentConfigError) throw error;
    reject('file', 'CONFIG.UNREADABLE');
  }
  return resolveDeploymentConfig(raw, { ...options, baseDir: dirname(resolve(file)) });
}
