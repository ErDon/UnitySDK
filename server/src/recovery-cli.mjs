import { open, stat } from 'node:fs/promises';
import { loadDeploymentConfig } from './deployment-config.mjs';
import { SqliteOrderStore } from './sqlite-orders.mjs';
import { PaymentRecovery } from './payment-recovery.mjs';
import { createOperatorEvidenceVerifier, signOperatorEvidence } from './operator-evidence.mjs';

async function readJson(path) {
  const file=await open(path,'r');
  try {
    const buffer=Buffer.alloc(32769);let total=0;
    while(total<buffer.length){const {bytesRead}=await file.read(buffer,total,buffer.length-total,null);if(!bytesRead)break;total+=bytesRead;}
    if(total>32768)throw new Error('RECOVERY.FILE_TOO_LARGE');
    return JSON.parse(new TextDecoder('utf-8',{fatal:true}).decode(buffer.subarray(0,total)));
  } finally {await file.close();}
}
let store;
try {
  const [command,...args]=process.argv.slice(2);
  const keyHex=process.env.SDK_OPERATOR_KEY_HEX,keyId=process.env.SDK_OPERATOR_KEY_ID ?? 'operator-v1';
  if(!/^[a-f0-9]{64}$/i.test(keyHex ?? ''))throw new Error('RECOVERY.KEY_REQUIRED');
  const key=Buffer.from(keyHex,'hex');
  if(command==='sign'&&args.length===1){
    const signed=signOperatorEvidence(await readJson(args[0]),keyId,key);
    // Validate metadata and expiry before producing an administrative authorization.
    createOperatorEvidenceVerifier({keys:new Map([[keyId,key]])})(signed);
    process.stdout.write(JSON.stringify(signed)+'\n');
  } else if(command==='payment'&&args.length>=2&&args.length<=4&&args.slice(2).every(x=>['--apply','--previous-worker-stopped'].includes(x))&&new Set(args.slice(2)).size===args.slice(2).length){
    const resolved=await loadDeploymentConfig(args[0],{requireSecrets:false});
    if(!resolved.paymentsEnabled)throw new Error('RECOVERY.PAYMENT_DISABLED');
    const {binding,orderPath}=resolved.configuration;
    // App ID is necessary to identify ownership; application/payment secrets are not.
    if(!binding.appId)throw new Error('RECOVERY.APP_ID_REQUIRED');
    if(!(await stat(orderPath)).isFile())throw new Error('RECOVERY.STORAGE_REQUIRED');
    store=new SqliteOrderStore(orderPath);
    const recovery=new PaymentRecovery({binding,store,verifyEvidence:createOperatorEvidenceVerifier({keys:new Map([[keyId,key]])})});
    const result=await recovery.apply(await readJson(args[1]),{dryRun:!args.includes('--apply'),previousWorkerStopped:args.includes('--previous-worker-stopped')});
    process.stdout.write(JSON.stringify(result)+'\n');
  } else throw new Error('RECOVERY.USAGE');
} catch(error){
  const code=typeof error?.code==='string'&&/^(RECOVERY|PAY|SERVER|CONFIG)\.[A-Z_]+$/.test(error.code)?error.code:
    /^RECOVERY\.[A-Z_]+$/.test(error?.message ?? '')?error.message:'RECOVERY.FAILED';
  process.stderr.write(JSON.stringify({errorCode:code})+'\n');process.exitCode=1;
} finally {store?.close();}
