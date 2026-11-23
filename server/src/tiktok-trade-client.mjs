import { OrderError } from './orders.mjs';

const validText = (value, limit = 256) => typeof value === 'string' && value.length > 0 && value.length <= limit &&
  value.trim() === value && !/[\u0000-\u001f\u007f]/.test(value);
const record = value => value !== null && typeof value === 'object' && !Array.isArray(value);

export function validateTikTokTradeRequest({ accessToken, tokenAmount, orderId, productId, productName, orderUrl, quantity, quantityUnit, imageUrl } = {}) {
  if (!validText(accessToken, 8192) || /\s/.test(accessToken) ||
      !Number.isSafeInteger(tokenAmount) || tokenAmount <= 0 ||
      !Number.isSafeInteger(quantity) || quantity <= 0 ||
      ![orderId, productId, productName, quantityUnit].every(value => validText(value)) ||
      !validText(orderUrl, 2048) || !validText(imageUrl, 2048))
    throw new TypeError('Invalid TikTok prepayment request');
  let image;
  try { image = new URL(imageUrl); } catch { throw new TypeError('Invalid TikTok product image URL'); }
  if (image.protocol !== 'https:' || image.username || image.password)
    throw new TypeError('Invalid TikTok product image URL');
}

// Server-to-server transport only; caller owns token custody, catalog, attempt tracking
// and persistence. Never retry create automatically after an uncertain response.
export function createTikTokTradeClient({ fetchImpl = fetch, timeoutMs = 8000 } = {}) {
  if (typeof fetchImpl !== 'function' || !Number.isSafeInteger(timeoutMs) || timeoutMs <= 0 || timeoutMs > 120000)
    throw new TypeError('Invalid TikTok trade client configuration');
  return Object.freeze({
    async create({ accessToken, tokenAmount, orderId, productId, productName, orderUrl, quantity, quantityUnit, imageUrl } = {}) {
      validateTikTokTradeRequest({ accessToken, tokenAmount, orderId, productId, productName, orderUrl, quantity, quantityUnit, imageUrl });
      const body = JSON.stringify({ token_type: 'BEANS', token_amount: tokenAmount, order_info: {
        order_id: orderId, product_id: productId, product_name: productName, order_url: orderUrl,
        quantity, quantity_unit: quantityUnit, image_url: imageUrl
      } });
      let response;
      try {
        response = await fetchImpl('https://open.tiktokapis.com/v2/minis/trade_order/create/', {
          method: 'POST', redirect: 'error', signal: AbortSignal.timeout(timeoutMs),
          headers: { Authorization: `Bearer ${accessToken}`, 'Content-Type': 'application/json', 'Cache-Control': 'no-store' }, body
        });
      } catch { throw new OrderError('PAY.PREPAYMENT_UNCERTAIN', 502); }
      if (!response.ok) {
        try { await response.body?.cancel(); } catch { }
        if (response.status === 401) throw new OrderError('PAY.PLATFORM_TOKEN_REJECTED', 401);
        // Even error responses are not evidence that no platform order was created.
        throw new OrderError('PAY.PREPAYMENT_UNCERTAIN', 502);
      }
      let reader;
      try {
        reader = response.body.getReader();
        const chunks = []; let length = 0;
        for (;;) {
          const { done, value } = await reader.read(); if (done) break;
          length += value.length; if (length > 16384) throw new Error(); chunks.push(value);
        }
        const data = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(Buffer.concat(chunks)));
        if (!record(data) || !record(data.error) || data.error.code !== 'ok' ||
            !record(data.data) || !validText(data.data.trade_order_id)) throw new Error();
        return Object.freeze({ tradeOrderId: data.data.trade_order_id });
      } catch { throw new OrderError('PAY.PREPAYMENT_UNCERTAIN', 502); }
      finally { try { await reader?.cancel(); } catch { } }
    }
  });
}
