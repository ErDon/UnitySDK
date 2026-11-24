import { OrderError } from './orders.mjs';
import { bindingKey } from './auth.mjs';
import { SqliteOrderStore } from './sqlite-orders.mjs';

const text = x => typeof x==='string' && x.length>0 && x.length<=256 && x.trim()===x && !/[\u0000-\u001f\u007f]/.test(x);
// Trusted administrative API. The verifier must authenticate a reviewed platform
// receipt/export. Never expose this method through the public game HTTP router.
export class PaymentRecovery {
  constructor({ binding, store, verifyEvidence, now = Date.now } = {}) {
    if (!(store instanceof SqliteOrderStore) || !['WeChat','Douyin','TikTok'].includes(binding?.platform) || typeof verifyEvidence!=='function')
      throw new TypeError('Invalid payment recovery configuration');
    this.binding=Object.freeze({...binding});this.scope=bindingKey(binding);this.store=store;this.verify=verifyEvidence;this.now=now;
    store.transaction(()=>{
      store.db.exec(`CREATE TABLE IF NOT EXISTS erdon_payment_recovery_schema(version INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS erdon_payment_recovery(evidence_id TEXT PRIMARY KEY,order_id TEXT NOT NULL,kind TEXT NOT NULL,
          envelope_hash TEXT NOT NULL,reviewer TEXT NOT NULL,source_reference TEXT NOT NULL,source_sha256 TEXT NOT NULL,applied_at INTEGER NOT NULL);`);
      const rows=store.db.prepare('SELECT version FROM erdon_payment_recovery_schema').all();
      if(rows.length>1 || (rows.length&&rows[0].version!==1))throw new OrderError('SERVER.SCHEMA_UNSUPPORTED',503);
      if(!rows.length)store.db.exec('INSERT INTO erdon_payment_recovery_schema VALUES(1)');
    });
  }
  async inspect(orderId) {
    const order=await this.store.get(orderId);
    if(!order || order.binding!==this.scope)throw new OrderError('PAY.ORDER_NOT_FOUND',404);
    return {orderId:order.orderId,subject:order.subject,productId:order.productId,amountMinor:order.amountMinor,currency:order.currency,state:order.state,
      audits:this.store.db.prepare('SELECT evidence_id AS evidenceId,kind,source_reference AS sourceReference,source_sha256 AS sourceSha256,reviewer,applied_at AS appliedAt FROM erdon_payment_recovery WHERE order_id=?').all(orderId)};
  }
  async apply(envelope,{dryRun=true,previousWorkerStopped=false}={}) {
    if(typeof dryRun!=='boolean'||typeof previousWorkerStopped!=='boolean')throw new TypeError('Invalid recovery options');
    const evidence=await this.verify(envelope);
    if(!evidence || !['payment-confirmed','prepayment-associated'].includes(evidence.kind) ||
        !['platform','appId','gameId','environment'].every(k=>evidence[k]===this.binding[k]) ||
        !text(evidence.orderId)||!text(evidence.transactionId)||!text(evidence.platformOrder))throw new OrderError('RECOVERY.EVIDENCE_MISMATCH',409);
    return this.store.transaction(()=>{
      const order=this.store.read(evidence.orderId);
      if(!order || order.binding!==this.scope)throw new OrderError('PAY.ORDER_NOT_FOUND',404);
      if(order.subject!==evidence.subject || order.productId!==evidence.productId || order.amountMinor!==evidence.amountMinor || order.currency!==evidence.currency)
        throw new OrderError('RECOVERY.EVIDENCE_MISMATCH',409);
      const prior=this.store.db.prepare('SELECT * FROM erdon_payment_recovery WHERE evidence_id=?').get(evidence.evidenceId);
      if(prior){
        if(prior.envelope_hash!==evidence.envelopeHash || prior.order_id!==order.orderId)throw new OrderError('RECOVERY.EVIDENCE_REUSED',409);
        return {orderId:order.orderId,state:order.state,duplicate:true,dryRun};
      }
      let associate=false;
      if(this.binding.platform==='WeChat') {
        const row=this.store.db.prepare('SELECT * FROM erdon_wechat_payments WHERE order_id=?').get(order.orderId);
        if(!row || row.binding!==order.binding || row.subject!==order.subject || row.platform_order!==evidence.platformOrder || evidence.kind!=='payment-confirmed')throw new OrderError('RECOVERY.EVIDENCE_MISMATCH',409);
        const sign=JSON.parse(row.sign_data);
        if(evidence.isSandbox!==(sign.env===1))throw new OrderError('RECOVERY.EVIDENCE_MISMATCH',409);
      } else if(this.binding.platform==='Douyin') {
        const row=this.store.db.prepare('SELECT * FROM erdon_douyin_payments WHERE order_id=?').get(order.orderId);
        if(!row || row.binding!==order.binding || row.subject!==order.subject || evidence.platformOrder!==order.orderId || evidence.kind!=='payment-confirmed' ||
            evidence.platformCurrency!==(row.route==='Android'?'CNY':'DIAMOND'))throw new OrderError('RECOVERY.EVIDENCE_MISMATCH',409);
      } else {
        if(evidence.transactionId!==evidence.platformOrder || typeof evidence.isSandbox!=='boolean' || !Number.isSafeInteger(evidence.tokenAmount)||evidence.tokenAmount<=0)
          throw new OrderError('RECOVERY.EVIDENCE_MISMATCH',409);
        const attempt=this.store.db.prepare('SELECT * FROM erdon_tiktok_attempts WHERE order_id=?').get(order.orderId);
        const row=this.store.db.prepare('SELECT * FROM erdon_tiktok_prepayments WHERE app_id=? AND trade_order_id=?').get(this.binding.appId,evidence.platformOrder);
        if(evidence.kind==='prepayment-associated') {
          if(previousWorkerStopped!==true)throw new OrderError('RECOVERY.WORKER_STOP_REQUIRED',409);
          if(!attempt || attempt.request_hash!==evidence.requestHash || order.state!=='Created')throw new OrderError('RECOVERY.EVIDENCE_MISMATCH',409);
          if(attempt.state==='Ready'&&attempt.trade_order_id!==evidence.platformOrder)throw new OrderError('PAY.PREPAYMENT_CONFLICT',409);
          associate=true;
        } else if(!attempt || attempt.state!=='Ready' || attempt.trade_order_id!==evidence.platformOrder || !row)throw new OrderError('PAY.PREPAYMENT_NOT_FOUND',409);
        if(row && (row.order_id!==order.orderId || row.binding!==order.binding || row.subject!==order.subject || row.open_id!==evidence.subject ||
            row.is_sandbox!==Number(evidence.isSandbox) || row.token_amount!==evidence.tokenAmount))throw new OrderError('PAY.PREPAYMENT_CONFLICT',409);
      }
      const transactionKey=JSON.stringify([this.binding.platform,this.binding.appId,evidence.transactionId]);
      if(!associate) {
        const owner=this.store.db.prepare('SELECT order_id FROM erdon_orders WHERE transaction_key=?').get(transactionKey);
        if(owner&&owner.order_id!==order.orderId)throw new OrderError('PAY.TRANSACTION_REUSED',409);
        if(order.transactionKey&&order.transactionKey!==transactionKey)throw new OrderError('PAY.TRANSACTION_CONFLICT',409);
      }
      const result={orderId:order.orderId,state:associate?order.state:order.state==='Created'?'Paid':order.state,associated:associate,dryRun};
      if(dryRun)return result;
      if(this.store.db.prepare('SELECT COUNT(*) AS n FROM erdon_payment_recovery').get().n>=this.store.capacity)throw new OrderError('SERVER.CAPACITY',503);
      if(associate) {
        const exists=this.store.db.prepare('SELECT 1 FROM erdon_tiktok_prepayments WHERE app_id=? AND trade_order_id=?').get(this.binding.appId,evidence.platformOrder);
        if(!exists){
          if(this.store.db.prepare('SELECT COUNT(*) AS n FROM erdon_tiktok_prepayments').get().n>=this.store.capacity)throw new OrderError('SERVER.CAPACITY',503);
          this.store.db.prepare('INSERT INTO erdon_tiktok_prepayments(app_id,trade_order_id,order_id,binding,subject,open_id,is_sandbox,token_amount) VALUES(?,?,?,?,?,?,?,?)')
            .run(this.binding.appId,evidence.platformOrder,order.orderId,order.binding,order.subject,order.subject,Number(evidence.isSandbox),evidence.tokenAmount);
        }
        this.store.db.prepare("UPDATE erdon_tiktok_attempts SET state='Ready',trade_order_id=? WHERE order_id=?").run(evidence.platformOrder,order.orderId);
      } else if(order.state==='Created')this.store.db.prepare("UPDATE erdon_orders SET state='Paid',transaction_key=? WHERE order_id=?").run(transactionKey,order.orderId);
      this.store.db.prepare('INSERT INTO erdon_payment_recovery VALUES(?,?,?,?,?,?,?,?)').run(evidence.evidenceId,order.orderId,evidence.kind,
        evidence.envelopeHash,evidence.reviewer,evidence.sourceReference,evidence.sourceSha256,this.now());
      return result;
    });
  }
}
