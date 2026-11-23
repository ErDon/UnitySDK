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

后续处理必须匹配用户、内部订单与平台订单；官方以服务端支付通知作为发货依据。见 [TikTok IAP 流程](https://developers.tiktok.com/docs/en/in-app-purchases)。下述 acceptWebhook 已提供通知到 Paid 的本地事务，下述独立 HTTP handler 已提供通知路由，尚未部署和接入真实发货，不能将这两个解析函数直接作为 OrderService.verifyPayment 或把 valid 映射为 paid。

本地运行 `node --test test/tiktok-webhook.test.mjs`（在 server 目录）：覆盖原始字节、正文/时间戳篡改、错误密钥、前后时间边界、重复头及输入限额，以及签名正确但应用/环境/事件不符、非法载荷和解析顺序。测试密钥只用于离线测试，不证明真实 TikTok 通知已通过。

## TikTok 预支付关联存储（服务端基础）

`TikTokPrepayments`（server/src/tiktok-prepayments.mjs）接收已有 SqliteOrderStore，使用同一数据库的独立版本表保存关联。`register({ binding, isSandbox, orderId, tradeOrderId, openId, tokenAmount })` 必须由服务端预支付流程调用：平台单号来自可信预支付响应，openId 来自服务端登录身份，Beans 数量来自服务端商品配置。它不是面向客户端的 API，也不会自行调用 TikTok 下单。

注册会检查游戏订单存在且属于完整 binding；用户 subject 从游戏订单读取。平台单号在同一 appId 下唯一，不能换绑游戏订单、用户、环境或价格。完全相同的重试返回旧关联；已付款订单禁止增加新关联。Created 订单可保存多个平台尝试记录，但这不授权并发扣款或自动重试。Beans 数量单独存储，不把它当作 GameOrder.AmountMinor 的法币分值。

`get({ binding, isSandbox, tradeOrderId })` 返回冻结快照，缺失或跨应用/游戏/环境返回 null；记录容量沿用订单存储的 capacity，满后拒绝新增且保留已有重试。实例不拥有数据库，统一由 orderStore.close() 关闭。数据库独立版本不支持时拒绝初始化；不会自动迁移未知格式。

错误包括 PAY.ORDER_NOT_FOUND、PAY.PREPAYMENT_CONFLICT、PAY.ORDER_ALREADY_PAID、SERVER.CAPACITY、SERVER.STORAGE_UNAVAILABLE。测试 `test/tiktok-prepayments.test.mjs` 验证恢复和隔离。尚需接可信预支付 API，不能据“关联存在”发货。

`acceptWebhook({ binding, isSandbox, rawBody, signature, clientSecret, nowSeconds?, toleranceSeconds? })` 先调用验签解析，再于同一 SQLite 事务中检查预支付关联、订单号、可用的 openId、游戏作用域和用户 subject。未通过验签/解析返回 `{ accepted: false, code }`；关联缺失、身份不符或交易冲突抛 OrderError，不改变订单。

首次匹配将 Created 改为 Paid，返回 `{ accepted: true, orderId, state, duplicate: false }`。重启后重复通知仍幂等；已 Fulfilled 订单保持 Fulfilled。另一平台交易尝试再支付同一订单返回 PAY.TRANSACTION_CONFLICT，必须对账处理，不能重复发货或自动声称退款。交易号被其他订单占用返回 PAY.TRANSACTION_REUSED。这些检查与状态写入在同一事务内，避免检查后再写入的竞争。

Paid 记录进入现有 pendingDelivery 队列，由 OrderService.fulfill 和游戏幂等发货接收方完成后续步骤；本方法不发放权益、不自动查询上游订单，HTTP 由下述 handler 承接。预支付记录仍须来自可信平台下单响应，不能开放 register 给客户端。新增测试覆盖 Paid/重启/已发货重复通知、签名正确但换单/换用户、两连接重复通知及第二笔交易冲突，不代表真实平台沙盒或生产验收。

## TikTok 预支付传输客户端

`createTikTokTradeClient`（server/src/tiktok-trade-client.mjs）的 `create` 接收服务器 accessToken、tokenAmount、orderId、productId、productName、orderUrl、quantity、quantityUnit、imageUrl。依照 [官方支付 API](https://developers.tiktok.com/docs/en/minis-payment-apis)固定向 TikTok HTTPS 创建订单地址发 POST，token_type 为 BEANS，禁止重定向；仅返回冻结的 tradeOrderId。

这只是服务器传输组件，调用方必须从可信商品配置和登录令牌保管服务取值，不能转发客户端金额或令牌。当前登录验证器仍只保留身份，不保管平台 token；该客户端尚未与登录、下单尝试持久化和 Unity 收银台接通。

本地参数错误在请求前拒绝；响应限制 16 KiB，默认请求超时 8 秒。401 返回 PAY.PLATFORM_TOKEN_REJECTED；网络异常、其他非成功 HTTP、业务错误或无效响应统一为 PAY.PREPAYMENT_UNCERTAIN，不输出平台正文或密钥。结果不确定时禁止自动重新创建订单；需要持久化尝试状态和对账，不能推断“平台未下单”。拿到平台单号后也必须可靠保存关联，不能因保存失败就重新下单。

`test/tiktok-trade-client.test.mjs` 使用注入响应检查端点、字段、参数拒绝、响应上限和不重试。没有访问真实支付接口，商户权限、令牌保管、档位校验及真实平台验收仍待完成。

## 持久化预支付编排

`TikTokPrepaymentFlow(payments, tradeClient)`（server/src/tiktok-prepayment-flow.mjs）把平台创建请求与关联存储连接起来。仅供可信服务器内部调用：`create` 接收 binding/isSandbox/openId、accessToken、orderId 及商品展示/Beans 参数；productId 从已有游戏订单读取。调用前必须完成玩家认证、目录定价和令牌身份匹配，尚未提供面向客户端的 HTTP 下单入口。

平台请求前以 SQLite 事务保存该游戏订单的 Pending 尝试；并发调用或重启后再调用均返回 PAY.PREPAYMENT_UNCERTAIN，不重复请求。请求成功后通过 completeAttempt 在同一 SQLite 事务内注册预支付关联并将尝试记为 Ready；相同参数再次调用复用平台单号。身份、价格或商品内容变化返回 PAY.PREPAYMENT_CONFLICT。令牌更新不改变已完成尝试的身份摘要。

关联写入与 Ready 更新现已合并为同一事务：任一步失败全部回滚，Pending 保留，后续不能自动再次下单。平台已创建但本地提交失败的对账恢复仍需处理。本地字段、令牌格式和图片 URL 通过共享校验后才占用 Pending；非法参数不留尝试记录，修正后可重新调用。占用后的平台请求错误仍保留 Pending，需要后续服务器对账/恢复工具处理。没有自动过期、自动解锁或取消后重新付款支持，不应删除记录强行重试。此行为优先避免重复创建，尚不代表完整支付重试流程。

`test/tiktok-prepayment-flow.test.mjs` 验证单次创建与复用、在途和失败阻断、非本游戏/已付款拒绝，以及 SQLite 关闭重开后的 Pending 保留。仍需接通令牌托管、平台档位校验、恢复和真实收银台。

## 服务端预支付授权与定价

`TikTokPrepaymentService({ orders, flow, catalog, credentials, isSandbox })` 对外业务方法为 `prepare(gameSession, orderId)`，不接受客户端价格、openId 或平台令牌。orders 与 flow 必须共享同一 SQLite 存储。服务先认证游戏会话和订单归属，检查 Created 状态，再从独立冻结的 TikTok 商品目录读取 Beans 数量、数量单位、名称与图片；productId 必须存在于游戏订单商品目录。

凭证提供器 `credentials.get(binding, subject)` 是服务器内部依赖，返回 `{ binding: bindingKey(binding), subject, accessToken, expiresAt }`，过期时间为毫秒。服务核对应用/游戏/环境作用域、用户和有效期，不符或读取异常返回 PAY.CREDENTIAL_UNAVAILABLE。异步读取后再次认证会话，随后交给持久化编排；响应仅含 orderId、tradeOrderId。当前身份映射使用 TikTok 登录验证器的 open_id 作为 subject。

这是授权服务，不是令牌库实现；仍需接入加密持久化和生命周期管理。商品档位须由服务端运营配置与平台要求核对。尚未开放预支付 HTTP 路由。测试 `test/tiktok-prepayment-service.test.mjs` 覆盖服务端定价快照、跨用户拒绝、会话撤销、错误/过期令牌和不泄露凭据。

## HTTP 通知入口装配

`createTikTokPaymentWebhookHandler(payments, options)` 位于 server/src/tiktok-webhook-http.mjs；options 必须包含服务端 binding、isSandbox、clientSecret，可注入 log、limiter 和不超过 65536 的 bodyLimit。只接收 `POST /v1/webhooks/tiktok/payment`，拒绝查询参数、非 JSON 及压缩正文；逐块限长保留原始字节，拒绝重复签名头。该 handler 需要由部署程序挂载，未自动加入默认 start.mjs。

成功及重复通知均在订单事务提交后响应 HTTP 200 `{ received: true, callId }`，不返回订单或用户详情。签名/时间失败为 401，其他无效载荷为 400，关联冲突为 409，超限为 413，限流为 429，存储失败为 503。日志含时间、接口、调用号、独立日志号和结果码，参数脱敏；日志回调异常不改变响应。未知服务器异常只返回 SERVER.INTERNAL。

部署必须使用 HTTPS 并设置请求/头读取超时；默认 limiter 是按连接来源 IP 的进程内实现，多实例应在网关或共享存储限流。handler 不信任客户端转发的 IP 头，也不提供客户端预支付注册或发货接口。当前只有回环 HTTP 验证，尚未注册平台回调地址、处理生产通知或完成退款业务。

测试：在 server 目录运行 `node --test`；tiktok-webhook-http.test.mjs 使用真实本机 HTTP 和 SQLite 检查提交后确认、重复、篡改、路由、编码、限额与日志隔离。

## TikTok Unity 底层支付网关

`IPlatformPaymentGateway.Launch(tradeOrderId, callback)` 是引擎/平台接入层接口，TikTok 实现为 `TikTokPaymentGateway`，编译受 ERDON_EXCLUDE_PAYMENTS 控制。官方 1.1.6 Runtime/Editor 均使用 `TT.Pay(TTPayParam)`，字段为 trade_order_id，继承的回调字段为 success/fail/complete。必须在官方初始化完成后的 Unity 主线程调用，并只传可信服务端返回的订单号。

网关成功返回 PaymentLaunchResult.AwaitServerConfirmation()：不伪造回执，不表示服务端 Paid 或 Fulfilled。失败保留平台数字码但不透传原始文本，返回 PAY.PLATFORM_REJECTED；调用异常为 PAY.LAUNCH_FAILED。并发面板返回 PAY.IN_PROGRESS，非法单号返回 PAY.INVALID_REQUEST。仅第一次 success/fail 生效；complete 本身不判成功。Dispose 抑制迟到回调，但不关闭收银台、不取消交易。

网关没有独立超时；若平台始终无终态，保持在途直到释放，避免重复拉起。上层仍需本地等待期限和服务端查单。它通过下述 PreparedPaymentLauncher 接到 IPaymentLauncher；预支付 HTTP 实现和平台工厂装配仍待完成。当前平台工厂仍未启用真实支付，生产阻断保持。

`tests/run-tiktok-tests.ps1` 使用官方两个 DLL 编译并运行 TikTokPaymentGatewayTests，验证参数映射、并发、重复回调、脱敏、异常与释放；无真实平台调用。

## 客户端编排步骤

`PreparedPaymentLauncher` 实现 IPaymentLauncher，拥有 IPrepaymentClient 与 IPlatformPaymentGateway。前者绑定当前游戏会话，通过 Prepare(orderId, callback) 返回 PrepaymentResult；返回订单号必须与输入一致，平台单号必须合法，才允许调用网关。重复预支付回调和重复终态均忽略，在途新调用返回 PAY.IN_PROGRESS，释放后抑制回调并释放依赖。两个接口的调用和回调均须在 Unity 主线程，HTTP 接入层负责派发。

它可以传给 VerifiedPayment。网关返回 RequiresServerQuery=true 时，编排仅查询服务器状态，不调用 Confirm；查询为 Created 返回 PAY.NOT_PAID，Paid 返回 PAY.FULFILLMENT_PENDING，Fulfilled 才成功。当前只查询一次，不自动轮询或重新拉起收银台。IPrepaymentClient 的 HTTPS 实现与服务端受认证入口尚待接入，不能仅构造启动器就认为完整支付可用。

1. 使用游戏 session、productId、稳定 requestId 创建或恢复订单。
2. 订单为 Fulfilled：直接返回成功，不再打开收银台。
3. 订单为 Paid：返回 PAY.FULFILLMENT_PENDING 和订单快照，不再打开收银台。
4. 订单为 Created：调用 IPaymentLauncher.Launch。
5. 平台返回回执时交 IOrderClient.Confirm；AwaitServerConfirmation 类型改用 Get 查询，成功必须以服务端 Fulfilled 为准。
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

`server/test/tiktok-payment-integration.test.mjs` 串接 OrderService、SQLite、预支付编排/传输、真实回环 HTTP 通知、验签和测试游戏发货接收方。它验证重复通知、接收方提交后确认丢失、订单与接收方数据库关闭重开、重试发货及已发货再次通知：最终一笔订单 Fulfilled、一次平台创建、一次权益增加。测试接收方在独立 SQLite 事务内同时记录 deliveryKey 和增加余额，避免“去重记录成功但没有发货”或相反情况。

该链路的上游平台响应、登录身份和游戏发货接收方都是测试实现，没有真实平台扣款或 Unity 收银台。不能将其算作工作计划的真实支付端到端验收。执行 `node --test test/tiktok-payment-integration.test.mjs`（server 目录），需允许本机回环网络。

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
