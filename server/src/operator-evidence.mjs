import { createHmac, createHash, timingSafeEqual } from 'node:crypto';
import { OrderError } from './orders.mjs';

const text = x => typeof x === 'string' && x.length > 0 && x.length <= 256 && x.trim() === x && !/[\u0000-\u001f\u007f]/.test(x);
// This signature is an OPERATOR attestation, not a platform payment signature.
// Keep these keys in the administrative tool, never in game clients or HTTP input.
export function signOperatorEvidence(payload, keyId, key) {
  if (!text(keyId) || !Buffer.isBuffer(key) || key.length !== 32) throw new TypeError('Invalid operator key');
  const raw = JSON.stringify(payload);
  if (typeof raw !== 'string' || Buffer.byteLength(raw) > 16384) throw new TypeError('Invalid operator evidence');
  return { payload: raw, keyId, signature: createHmac('sha256', key).update(raw).digest('hex') };
}
export function createOperatorEvidenceVerifier({ keys, now = Date.now } = {}) {
  if (!(keys instanceof Map) || !keys.size || typeof now !== 'function') throw new TypeError('Invalid operator verifier');
  const copied = new Map();
  for (const [id,key] of keys) {
    if (!text(id) || !Buffer.isBuffer(key) || key.length !== 32) throw new TypeError('Invalid operator key');
    copied.set(id,Buffer.from(key));
  }
  return envelope => {
    try {
      if (typeof envelope?.payload !== 'string' || Buffer.byteLength(envelope.payload)>16384 ||
          !copied.has(envelope.keyId) || !/^[a-f0-9]{64}$/.test(envelope.signature ?? '')) throw new Error();
      const digest = createHmac('sha256',copied.get(envelope.keyId)).update(envelope.payload).digest();
      if (!timingSafeEqual(digest,Buffer.from(envelope.signature,'hex'))) throw new Error();
      const data=JSON.parse(envelope.payload), time=now();
      if (!data || data.schemaVersion!==1 || !['authorization-revoked','payment-confirmed','prepayment-associated'].includes(data.kind) ||
          !['evidenceId','reviewer','sourceReference','appId','subject','platform','environment','gameId'].every(k=>text(data[k])) ||
          !/^[a-f0-9]{64}$/.test(data.sourceSha256 ?? '') || !Number.isSafeInteger(time) ||
          !Number.isSafeInteger(data.issuedAt) || !Number.isSafeInteger(data.expiresAt) || data.issuedAt>time+300000 ||
          data.expiresAt<=time || data.expiresAt<=data.issuedAt || data.expiresAt-data.issuedAt>86400000) throw new Error();
      return Object.freeze({...data, envelopeHash:createHash('sha256').update(envelope.payload).digest('hex')});
    } catch { throw new OrderError('RECOVERY.EVIDENCE_INVALID',400); }
  };
}
