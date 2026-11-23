# 支付编排（开发中）

VerifiedPayment 实现 IPay，组合 IOrderClient 与 IPaymentLauncher。它负责客户端流程，不能替代服务端平台下单、验签与发货。已接 SDK.Pay 聚合及 Editor Mock 配置预览，真实平台收银台尚未接入。

## TikTok 服务端验签基础（尚未接通订单）

`server/src/tiktok-webhook.mjs` 提供 `verifyTikTokWebhook`，按 [TikTok 官方验签协议](https://developers.tiktok.com/docs/en/webhooks-verification)使用时间戳、点号和原始请求体计算 HMAC-SHA256，以恒定时间比较摘要。以下是服务器代码，不能放进 Unity 客户端：

```javascript
import { verifyTikTokWebhook } from './src/tiktok-webhook.mjs';
const checked = verifyTikTokWebhook({
  rawBody, // HTTP 层限流读取的原始 Buffer，不能 JSON.parse 后重新序列化
  signature: request.headers['tiktok-signature'],
  clientSecret: secrets.tiktokClientSecret // 仅来自服务器密钥配置
});
if (!checked.valid) {
  // 拒绝该事件；日志只保留 checked.code，不记录正文、签名或密钥。
}
// valid 仅证明签名与时间窗口通过，不能在这里直接发货。
```

此函数要求非空 Buffer 且不超过 64 KiB，默认允许前后 300 秒，可缩短窗口；HTTP 层仍须在读取过程中限长。缺失、重复 t/s、额外字段、格式错误或超长头均拒绝。返回 WEBHOOK.INVALID_BODY、WEBHOOK.INVALID_SIGNATURE 或 WEBHOOK.TIMESTAMP_OUT_OF_RANGE；无效服务器配置抛 TypeError。时间窗口内的重复请求仍能验签成功，不能替代持久化幂等处理。

`readTikTokPaymentWebhook({ ...验签参数, clientKey, isSandbox })` 在验签后解析支付通知；clientKey 与布尔型 isSandbox 必须由服务端显式提供。它拒绝错误应用、环境不匹配、非支付成功事件、非法 UTF-8/JSON 和缺失或非法订单字段，分别返回 WEBHOOK.APP_MISMATCH、WEBHOOK.ENVIRONMENT_MISMATCH、WEBHOOK.EVENT_UNSUPPORTED 或 WEBHOOK.INVALID_PAYLOAD。退款通知不会被当作购买成功，目前也不处理退款业务。

成功仅返回冻结的 notification：clientKey、isSandbox、orderId、tradeOrderId、userOpenId、createdAtSeconds，不透传正文额外字段。官方样例的 user_openid 可以为空，因此用户归属仍必须通过服务端可信的预支付关联记录校验，不能只信通知里的订单号。

后续处理必须匹配用户、内部订单与平台订单；官方以服务端支付通知作为发货依据。见 [TikTok IAP 流程](https://developers.tiktok.com/docs/en/in-app-purchases)。当前尚无 webhook HTTP 路由和通知到真实发货接入，不能将这两个函数直接作为 OrderService.verifyPayment 或把 valid 映射为 paid。

本地运行 `node --test test/tiktok-webhook.test.mjs`（在 server 目录）：覆盖原始字节、正文/时间戳篡改、错误密钥、前后时间边界、重复头及输入限额，以及签名正确但应用/环境/事件不符、非法载荷和解析顺序。测试密钥只用于离线测试，不证明真实 TikTok 通知已通过。

## TikTok 预支付关联存储（服务端基础）

`TikTokPrepayments`（server/src/tiktok-prepayments.mjs）接收已有 SqliteOrderStore，使用同一数据库的独立版本表保存关联。`register({ binding, isSandbox, orderId, tradeOrderId, openId, tokenAmount })` 必须由服务端预支付流程调用：平台单号来自可信预支付响应，openId 来自服务端登录身份，Beans 数量来自服务端商品配置。它不是面向客户端的 API，也不会自行调用 TikTok 下单。

注册会检查游戏订单存在且属于完整 binding；用户 subject 从游戏订单读取。平台单号在同一 appId 下唯一，不能换绑游戏订单、用户、环境或价格。完全相同的重试返回旧关联；已付款订单禁止增加新关联。Created 订单可保存多个平台尝试记录，但这不授权并发扣款或自动重试。Beans 数量单独存储，不把它当作 GameOrder.AmountMinor 的法币分值。

`get({ binding, isSandbox, tradeOrderId })` 返回冻结快照，缺失或跨应用/游戏/环境返回 null；记录容量沿用订单存储的 capacity，满后拒绝新增且保留已有重试。实例不拥有数据库，统一由 orderStore.close() 关闭。数据库独立版本不支持时拒绝初始化；不会自动迁移未知格式。

错误包括 PAY.ORDER_NOT_FOUND、PAY.PREPAYMENT_CONFLICT、PAY.ORDER_ALREADY_PAID、SERVER.CAPACITY、SERVER.STORAGE_UNAVAILABLE。测试 `test/tiktok-prepayments.test.mjs` 验证恢复和隔离。尚需接可信预支付 API、验签通知消费与订单状态原子更新，不能据“关联存在”发货。

## 状态与步骤

1. 使用游戏 session、productId、稳定 requestId 创建或恢复订单。
2. 订单为 Fulfilled：直接返回成功，不再打开收银台。
3. 订单为 Paid：返回 PAY.FULFILLMENT_PENDING 和订单快照，不再打开收银台。
4. 订单为 Created：调用 IPaymentLauncher.Launch。
5. 平台回调提交后，将回执交 IOrderClient.Confirm；成功必须以服务端 Fulfilled 为准。
6. 平台取消、失败或抛异常时，先 Get 查询服务端状态，处理客户端取消与服务端支付完成同时发生的情况。

不直接发放道具，不自动轮询或自动重复付款。Paid/网络结果不确定时保留 requestId 与 orderId，通过查询或同一个 requestId 恢复；禁止把重试变成新购买。

## 接口示例

```csharp
using ErDon.UnitySDK;

// orderClient、platformLauncher、mainThreadPost 由接入层创建。
// mainThreadPost 将委托排入 Unity 主线程队列，不能直接在任意平台回调线程执行。
IPay payment = new VerifiedPayment(orderClient, platformLauncher, mainThreadPost);
payment.Pay(new PaymentRequest(gameSession, "coins", savedPurchaseRequestId), result => {
    if (result.Success) {
        // 服务端已标记 Fulfilled；同步游戏权益，不在客户端重复发奖。
    } else if (result.Error.Code == "PAY.FULFILLMENT_PENDING") {
        // 显示已支付、等待到账；保留 result.Order.OrderId。
    } else {
        // 记录安全错误码及 ServerCallId，等待用户操作或查单。
    }
});
```

IPaymentLauncher.Launch 接收服务端订单快照。真实 launcher 必须通过可信服务器取得该平台需要的支付参数，再调用官方 SDK；不能用客户端金额签名。Submitted(evidence) 仅表示可继续服务端验证，不代表付款成功。失败使用 PaymentLaunchResult.Fail(SdkError)，携带脱敏平台码。未实现任何默认成功 launcher。

## 生命周期与诊断

VerifiedPayment 拥有两个依赖，Dispose 分别释放；相同对象实现两个接口只释放一次。在途已入队回调在 Dispose 后忽略。每阶段只接受第一次回调，防止重复平台消息重复确认；已有流程未完成时返回 PAY.IN_PROGRESS。调用入口要求主线程，平台结果通过提供的 post 派发。

VerifiedPayment 自身不取消平台交易；聚合层 SDK.Pay 提供默认 60 秒超时和 SDK.CancelPay 本地取消等待，HTTP 传输另有自身超时。不能把 Dispose 视为取消已经进行的支付。上层 SDK.Pay 提供 CallId/TraceId、操作与阶段及格式化日志，记录服务端 ServerCallId；请求内容整体脱敏，不输出 session、回执或任意异常文本。

常见流程错误：PAY.INVALID_REQUEST、PAY.IN_PROGRESS、PAY.CREATE_FAILED、PAY.LAUNCH_FAILED、PAY.VERIFICATION_FAILED、PAY.INVALID_RESPONSE、PAY.NOT_PAID、PAY.FULFILLMENT_PENDING。Created 与确认结果必须保持相同订单/商品/金额/币种，未知状态或换单拒绝。服务端已完成发货优先于平台取消提示，查询失败不会伪造取消成功。

PaymentResult.Success 只有服务端订单状态为 Fulfilled 时成立。Order 可能为空（创建或网络失败），读取前检查。ServerCallId 来自服务端订单响应，不能将它和未来客户端调用编号混用。

## 验证范围

PaymentPipelineTests 覆盖重复回调、主线程队列、并发支付拒绝、平台成功后仍等待服务端、Paid/Fulfilled 恢复不再拉起收银台、取消竞争、订单被替换及释放失效。订单与 launcher 均使用测试实现，不等同于真实支付验收。


## 游戏上层调用

通过 SdkConfig.PayEnabled 开启支付；工厂实现 IPayProviderFactory.CreatePay(post)，返回全新 VerifiedPayment。关闭模块时不创建支付 provider。也可在 UnitySdkHost.Initialize 的 payProvider 参数传入实现；工厂方式更适合按配置装配。

```csharp
using ErDon.UnitySDK;

SDK.Pay(new PaymentRequest(gameSession, "coins", savedPurchaseRequestId), result => {
    if (result.Success) {
        // 服务端已完成发货，只同步权益。
    } else {
        UnityEngine.Debug.LogWarning(result.Error.Code + ": " + result.Error.Reason + " callId=" + result.CallId);
        // TIMEOUT/CANCELLED 不能解释为未扣费。保留购买编号，查单后处理。
    }
}, timeoutMilliseconds: 60000);
// SDK.CancelPay(); // 只结束本地等待，不关闭平台收银台、不退款。
```

SDK.Pay 对未初始化、模块关闭、缺少 provider、非法请求及并发调用返回明确错误；回调异常被隔离。超时由 SDK.Tick/Unity Update 驱动，迟到结果不再次触发上层回调。超时/取消后，底层 VerifiedPayment 可能仍在核验，继续支付可能返回 PAY.IN_PROGRESS；不得用新 requestId 绕过等待或重复扣款。重建 SDK 也不撤销服务端订单，恢复必须使用原编号。

Tools → ErDon SDK → 配置与预览：勾选支付，选择支付模拟结果，再初始化 Mock。可预览 Fulfilled、Pending、Cancelled、Timeout；按钮不创建真实订单。修改开关或模拟结果后需重新初始化。生产 Mock 构建继续阻断，真实支付平台亦未放行。
