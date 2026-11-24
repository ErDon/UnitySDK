import { randomUUID } from 'node:crypto';
import { OrderService, OrderError } from './orders.mjs';

// Server-only worker. Receiver must atomically deduplicate deliveryKey and grant goods.
// Stop and await the worker before closing its order store.
export class DeliveryWorker {
  constructor({ orders, deliver, intervalMs = 3000, batchSize = 100, log = console.info } = {}) {
    if (!(orders instanceof OrderService) || typeof deliver !== 'function' || typeof log !== 'function' ||
        !Number.isSafeInteger(intervalMs) || intervalMs < 100 || intervalMs > 3600000 ||
        !Number.isSafeInteger(batchSize) || batchSize < 1 || batchSize > 10000 || typeof orders.store.pendingDelivery !== 'function')
      throw new TypeError('Invalid delivery worker configuration');
    this.orders = orders; this.deliver = deliver; this.intervalMs = intervalMs; this.batchSize = batchSize; this.log = log;
    this.stopped = false; this.started = false; this.timer = null; this.running = null;
    this.cursor = 0;
  }
  start() {
    if (this.stopped) throw new Error('Delivery worker was stopped');
    if (this.started) return;
    this.started = true; this.schedule();
  }
  schedule() {
    if (this.stopped || !this.started) return;
    this.timer = setTimeout(async () => {
      this.timer = null;
      try { await this.runOnce(); } finally { this.schedule(); }
    }, this.intervalMs);
    this.timer.unref?.();
  }
  runOnce() {
    if (this.stopped) return Promise.resolve();
    if (this.running) return this.running;
    this.running = this.drain().finally(() => { this.running = null; });
    return this.running;
  }
  async drain() {
    const callId = randomUUID(); let processed = 0, failed = 0, result = 'OK';
    try {
      const pending = (await this.orders.store.pendingDelivery()).filter(order => order.binding === this.orders.scope);
      // Rotate the starting point so a failing receiver cannot starve later orders.
      const start = pending.length ? this.cursor % pending.length : 0;
      const count = Math.min(pending.length, this.batchSize);
      for (let i = 0; i < count && !this.stopped; i++) {
        const order = pending[(start + i) % pending.length];
        try { await this.orders.fulfill(order.orderId, this.deliver); processed++; }
        catch { failed++; }
      }
      this.cursor = pending.length ? (start + count) % pending.length : 0;
      if (failed) result = 'PAY.DELIVERY_RETRY_PENDING';
    } catch (error) {
      result = error instanceof OrderError ? error.code : 'PAY.DELIVERY_WORKER_FAILED';
    } finally {
      try { this.log(JSON.stringify({ time: new Date().toISOString(), interface: 'Payment.DeliverPending',
        callId, logId: randomUUID(), params: { batchSize: this.batchSize }, processed, failed, result })); } catch { }
    }
  }
  async stop() {
    this.stopped = true; this.started = false;
    if (this.timer) { clearTimeout(this.timer); this.timer = null; }
    await this.running;
  }
}
