# 支付编排（开发中）

VerifiedPayment 实现 IPay，组合 IOrderClient 与 IPaymentLauncher。它负责客户端流程，不能替代服务端平台下单、验签与发货。已接 SDK.Pay 聚合、Editor Mock 预览及三平台收银台网关。抖音/TikTok 已有显式支付工厂和服务端组合，微信服务端链路与全部真实扣款仍待验证。

## TikTok 服务端验签基础

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

`TikTokPrepaymentFlow(payments, tradeClient)`（server/src/tiktok-prepayment-flow.mjs）把平台创建请求与关联存储连接起来。仅供可信服务器内部调用：`create` 接收 binding/isSandbox/openId、accessToken、orderId 及商品展示/Beans 参数；productId 从已有游戏订单读取。调用前必须完成玩家认证、目录定价和令牌身份匹配，可通过下述可选 HTTP 路由提供服务。

平台请求前以 SQLite 事务保存该游戏订单的 Pending 尝试；并发调用或重启后再调用均返回 PAY.PREPAYMENT_UNCERTAIN，不重复请求。请求成功后通过 completeAttempt 在同一 SQLite 事务内注册预支付关联并将尝试记为 Ready；相同参数再次调用复用平台单号。身份、价格或商品内容变化返回 PAY.PREPAYMENT_CONFLICT。令牌更新不改变已完成尝试的身份摘要。

关联写入与 Ready 更新现已合并为同一事务：任一步失败全部回滚，Pending 保留，后续不能自动再次下单。平台已创建但本地提交失败的对账恢复仍需处理。本地字段、令牌格式和图片 URL 通过共享校验后才占用 Pending；非法参数不留尝试记录，修正后可重新调用。占用后的平台请求错误仍保留 Pending，需要后续服务器对账/恢复工具处理。没有自动过期、自动解锁或取消后重新付款支持，不应删除记录强行重试。此行为优先避免重复创建，尚不代表完整支付重试流程。

`test/tiktok-prepayment-flow.test.mjs` 验证单次创建与复用、在途和失败阻断、非本游戏/已付款拒绝，以及 SQLite 关闭重开后的 Pending 保留。仍需接通令牌托管、平台档位校验、恢复和真实收银台。

## 服务端预支付授权与定价

`TikTokPrepaymentService({ orders, flow, catalog, credentials, isSandbox })` 对外业务方法为 `prepare(gameSession, orderId)`，不接受客户端价格、openId 或平台令牌。orders 与 flow 必须共享同一 SQLite 存储。服务先认证游戏会话和订单归属，检查 Created 状态，再从独立冻结的 TikTok 商品目录读取 Beans 数量、数量单位、名称与图片；productId 必须存在于游戏订单商品目录。

凭证提供器 `credentials.get(binding, subject)` 是服务器内部依赖，返回 `{ binding: bindingKey(binding), subject, accessToken, expiresAt }`，过期时间为毫秒。服务核对应用/游戏/环境作用域、用户和有效期，不符或读取异常返回 PAY.CREDENTIAL_UNAVAILABLE。异步读取后再次认证会话，随后交给持久化编排；响应仅含 orderId、tradeOrderId。当前身份映射使用 TikTok 登录验证器的 open_id 作为 subject。

该授权服务可注入下述 TikTokCredentialStore；登录兑换写入和按需刷新已接入 TikTokApplication，授权撤销联动仍待实现。商品档位须由服务端运营配置与平台要求核对。订单 handler 的可选预支付路由见下文，默认未开启。测试 `test/tiktok-prepayment-service.test.mjs` 覆盖服务端定价快照、跨用户拒绝、会话撤销、错误/过期令牌和不泄露凭据。

## 平台令牌加密存储

### 待发货任务

服务端 `DeliveryWorker` 接收 `{ orders, deliver, intervalMs, batchSize, log }`。默认每 3 秒最多处理 100 个当前应用作用域的 Paid 订单，通过 OrderService.fulfill 调用业务发货回调；失败订单保留 Paid，后续轮次重试，分批轮转避免一直失败的订单阻塞后面的订单。runOnce 可手动执行，重叠调用合并为同一次执行；start 启用定时任务，stop 停止后续任务并等待正在发货的调用完成。

TikTok 组合服务可通过 `createTikTokApplication({ ..., deliver, deliveryIntervalMs: 3000, deliveryBatchSize: 100 })` 自动启动任务；不传 deliver 保留手工发货模式。关闭时先排空 HTTP 请求，再 `await app.close()`，它会等待任务结束再关闭数据库。deliver(message) 只在业务权益持久化成功后返回 true，必须将 deliveryKey 去重与发货置于同一数据库事务。SDK 不持有游戏背包或钱包，不能替游戏决定权益写入位置。接收方应设置自己的请求超时；SDK 不在发货结果未知时强制启动重叠调用。

日志包含接口、时间、callId/logId、批量上限、处理/失败数量与统一错误，不输出用户或接收方异常。任务使用本机定时器，无分布式抢占锁；多实例必须依赖接收方原子去重，生产监控与外部服务部署另行配置。

### 微信道具直购网关

`WeChatPaymentGateway` 实现 `IPlatformPaymentGateway<WeChatPaymentRequest>`，调用 WX.RequestMidasPaymentGameItem。请求包含服务器返回的 SignData、PaySignature、SessionSignature，不可变且 ToString 脱敏；签名原文不裁剪、不解析重排，价格和签名均由服务器负责。此网关不覆盖游戏币充值、好友代付或其他虚拟支付 API。

支付签名按官方类型文档中的 SHA-256 十六进制格式校验；用户态签名仅检查非空、可见 ASCII 和长度上限，作为不透明服务端数据传递。成功回调要求非空且 errCode=0，仅返回 AwaitServerConfirmation；其他结果保留数值平台码并返回 PAY.PLATFORM_REJECTED，不透传原始 errMsg。并发、重复回调、异常及关闭后回调均受保护。

已对本地 0.1.23 和官方固定提交 915d4bf 的 0.1.1 转换包分别运行官方类型/入口编译与注入行为测试。接口依据 [官方 API 类型声明](https://github.com/wechat-miniprogram/minigame-api-typings/blob/master/types/wx/lib.wx.api.d.ts) 和固定 SDK 的 WX.cs；当前官方网页及其用户态签名参考链接访问失败，用户态签名服务尚未实现，未猜测算法。服务端签名/凭据托管、订单映射、通知核验及工厂装配仍待完成，配置入口仍阻断微信支付。

### 抖音支付网关

`DouyinPaymentGateway` 实现 `IPlatformPaymentGateway<DouyinPaymentRequest>`，显式选择 Android 或 IosCustomerService 路由；前者使用 TT.RequestGamePayment，后者使用 TT.OpenAwemeCustomerService。参数和回调已对照 [官方 C# 支付文档](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/api/c-api/payment/payment-request) 及本地官方 6.9.7 DLL 核验。

DouyinPaymentRequest 为不可变参数快照：OrderId、ZoneId、ExtraInfo，以及游戏币数量 BuyQuantity 或直购价格 AmountMinor / ProductName / DirectPurchase。直购必须 BuyQuantity=0；游戏币模式不填直购金额/商品名。订单号、数量和价格必须取自认证后的游戏服务端，不能以客户端商品输入为准。网关不会换算价格档位或验证后台开通资格；直购会检查相应 CanIUse 能力，缺能力返回 CAPABILITY.NOT_SUPPORTED，不自动降级为其他付费场景。

成功回调仅返回 AwaitServerConfirmation，不生成支付证据。取消码 -2 映射 PAY.USER_CANCELLED，其余失败返回 PAY.PLATFORM_REJECTED 并保留数字平台码，原始文本不传给上层。并发/重复回调与 Dispose 后迟到回调受保护，关闭网关不撤销平台交易。抖音道具直购已完成受认证参数映射、签名通知入账、服务装配与显式支付工厂，完整示例见 [抖音支付接入](douyin-payments.md)。主动平台查单、游戏币扣除与真实支付仍待完成，不代表已可上线。既有非泛型 IPlatformPaymentGateway 保留为字符串请求特化，TikTok 调用方式不变。

### 平台查单传输

`createTikTokTradeClient().query({ accessToken, tradeOrderId })` 使用服务端保管的用户访问令牌，调用固定 HTTPS POST `/v2/minis/trade_order/query/`，返回冻结的 `{ tradeOrderId, status }`。只接受与请求相同的平台单号及 PENDING / SUCCESS 状态；未知状态、格式错误或超过 16 KiB 的响应返回 PAY.QUERY_RESPONSE_INVALID，网络或 HTTP 失败返回 PAY.QUERY_UNAVAILABLE，HTTP 401 返回 PAY.PLATFORM_TOKEN_REJECTED。禁止重定向，不自动重试，不回传上游消息或令牌。

依据 [Payment APIs](https://developers.tiktok.com/docs/en/minis-payment-apis) 和 [In-App Purchases](https://developers.tiktok.com/docs/en/in-app-purchases)（2026-11-24 核对），此能力用于辅助核验，SUCCESS 不替代签名通知，也不直接执行 markPaid 或发货。

组合应用提供 `POST /v1/orders/{gameOrderId}/query`，使用 Bearer 游戏会话，JSON 正文传 `{}`。服务先检查订单归属，再读取 Ready 预支付记录及匹配的应用/环境/沙箱/用户关联，使用服务器保存的访问令牌查询；正文中的平台单号和令牌均不采用。返回 `{ query: { orderId, platformStatus, state }, callId }`，其中 platformStatus 是平台辅助状态，state 是当前本地订单状态，只有 Fulfilled 表示服务端确认发货完成。无预支付关联返回 PAY.PREPAYMENT_NOT_FOUND / 404；凭据缺失/过期返回 PAY.CREDENTIAL_UNAVAILABLE / 503。凭据读取后、上游响应后均重新验证会话。未装配预支付服务的订单 handler 不开放该路由。

此接口没有后台定时核验或 Unity 诊断客户端，平台查询也不能恢复尚未取得 trade_order_id 的不确定预支付请求；不会为恢复操作重新创建平台订单。

### TikTok 服务端组合入口

`server/src/tiktok-application.mjs` 的 `createTikTokApplication` 将登录兑换、凭据加密保存、游戏订单、预支付和签名通知组合成一个 HTTP handler。每个实例绑定一个游戏/应用/环境，配置中的 catalog 同时提供订单展示金额和 TikTok Beans 商品参数；两者必须由运营配置核对，SDK 不推算换算比例。

```js
import { createServer } from 'node:http';
import { createTikTokApplication } from './src/tiktok-application.mjs';

// deployment 由部署层加载；不得放进 Unity 工程或提交密钥到 Git。
const app = createTikTokApplication({
  binding: deployment.binding, // gameId, appId, environment, platform: 'TikTok', secret
  isSandbox: true,
  authPath: './data/auth.sqlite',
  orderPath: './data/orders.sqlite',
  credentialPath: './data/credentials.sqlite',
  keys: deployment.keys, // Map<keyId, 32-byte Buffer>
  currentKeyId: deployment.currentKeyId,
  catalog: deployment.catalog // productId, amountMinor, currency, tokenAmount,
  // productName, quantity, quantityUnit, imageUrl
});
const server = createServer({ requestTimeout: 10000, headersTimeout: 5000, maxHeaderSize: 8192 }, app.handler);
server.listen(8787, '127.0.0.1'); // Unity / 平台通知通过部署层 HTTPS 入口访问。
// 退出时先停止接收并排空 HTTP 请求，再关闭三个数据库及清除存储持有的密钥副本。
process.once('SIGTERM', () => server.close(() => app.close()));
```

目录需预先创建。入口提供 POST /v1/session、订单路由（含 prepare）和 POST /v1/webhooks/tiktok/payment；不会创建公开发货路由，不接受客户端成功回执作为支付依据。服务器工作进程可读取 `app.pendingDelivery()`，再调用 `app.orders.fulfill(orderId, deliver)`；deliver 必须在业务数据库中将 deliveryKey 去重和权益增加放在同一事务内。

此入口拥有其创建的数据库，构造中途失败会释放已创建资源；close 可重复调用。调用者必须先排空在途请求再 await close。可选定时发货已实现；按需令牌刷新已接入；公网部署和授权撤销联动尚未完成。原有 npm start 仍为仅登录参考服务，不会隐式启用支付。组合测试使用模拟 OAuth/支付响应和本地签名通知，不代表真实扣款。

### TikTok 服务启动

新增 `npm run start:tiktok`。设置 `SDK_TIKTOK_CONFIG` 为可信部署配置模块的绝对路径（.mjs），模块 `export default` 上述 createTikTokApplication 配置，含服务端密钥环与可选业务 deliver 回调。SDK_PORT 可选，默认 8787。配置模块能执行代码，只能由部署人员维护，不能指向用户上传文件。数据目录需要预先创建，密钥不得放进 Unity 项目或提交到仓库。

```powershell
$env:SDK_TIKTOK_CONFIG = 'D:\GameServer\tiktok.config.mjs'
$env:SDK_PORT = '8787'
npm run start:tiktok
```

服务仅监听 127.0.0.1，由部署侧 HTTPS 反向代理转发；SIGINT/SIGTERM 停止监听、排空请求后等待发货任务并关闭存储。启动失败只输出固定诊断，避免导入错误暴露配置内容。也可通过 startTikTokServer(configuration, { port }) 嵌入自己的进程，await 返回实例的 stop() 关闭。现有每进程限流按直连来源计数，代理部署需要配套网关限流，不能直接信任任意 X-Forwarded-For。TLS、系统服务安装、实际账号密钥与业务权益存储不由示例自动创建。

`TikTokCredentialStore(path, { keys, currentKeyId, now?, capacity? })`（server/src/tiktok-credential-store.mjs）实现凭证提供器的 get，并提供 put/remove/close。keys 为密钥 ID 到 32 字节 Buffer 的 Map；调用方从部署密钥服务加载，不放进 Unity、JSON 配置或 Git。存储复制密钥 Buffer，关闭时清理自己的副本，不改调用方的 Buffer；不承诺 JavaScript 字符串在内存中安全擦除。

put(binding, subject, { accessToken, expiresAt }) 以 AES-256-GCM 和随机 12 字节 nonce 加密；认证附加数据绑定版本、完整作用域、用户、有效期与密钥 ID。数据库保存密文、nonce、标签及必要关联元数据，应用/用户标识不加密。get 返回未过期的冻结凭据；缺失/过期返回 null，错误密钥、篡改、存储故障统一 PAY.CREDENTIAL_UNAVAILABLE。容量满拒绝新增，写入时清理过期记录。

轮换时保留旧 keyId 的密钥并设置新的 currentKeyId；旧记录可读，之后 put 使用新密钥。没有自动批量重加密；TikTokCredentials 在支付读取时按需刷新平台 token。删除密钥前必须处理其仍在使用的记录。SQLite 是单机持久化方案，数据库备份与密钥生命周期由部署管理，不能把数据库加密等同于整个服务已完成安全验收。

可将同一 store 作为 TikTokPrepaymentService 的 credentials，并通过 `createTikTokVerifier({ credentialStore: store })` 注入登录验证器。验证成功后先等待令牌保存，再由 AuthService 签发游戏会话；不注入时保留仅身份兑换行为。有效期从上游请求开始前计算，避免网络耗时延长令牌寿命，过期或溢出拒绝。写入异常统一返回 HTTP 503 / AUTH.CREDENTIAL_STORAGE_FAILED，客户端应稍后取得新的登录 code，不重用已经提交的 code。平台访问令牌和刷新令牌不返回客户端；同时加密保存上游提供的刷新令牌；撤销联动尚未完成。

默认服务器尚未配置密钥或加载此存储。`test/tiktok-credential-store.test.mjs` 覆盖重开恢复、磁盘无测试令牌明文、AAD 篡改、错误密钥、轮换、容量、过期及关闭；`test/tiktok.test.mjs` 另验证保存先于签发会话、作用域隔离、写入失败不签发、凭证防重放及有效期边界。真实 OAuth 与支付联调仍待完成。

## HTTP 通知入口装配

订单接口装配时传入 `createOrderHandler(orders, { prepayments: prepaymentService })`，启用 `POST /v1/orders/{orderId}/prepare`。prepaymentService 必须使用同一个 orders 实例；不传时路由返回 404。请求使用游戏会话 `Authorization: Bearer ...`，JSON 正文为 `{}`；请求正文的额外字段全部不用于下单。成功响应为 `{ prepayment: { orderId, tradeOrderId }, callId }`，没有平台令牌。服务器仍须配置凭证提供器和商品目录，默认 start.mjs 未装配它。

该路由复用订单接口的限流、请求限额、HTTP 错误和脱敏日志，接口名为 Order.Prepare。其他玩家订单返回 404，会话无效/撤销返回 401；服务端不接受通过正文换订单、改价或换平台身份。UnityPrepaymentClient 已实现该协议，HTTPS 运行联调仍待完成。

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

`UnityPrepaymentClient(host, ordersEndpoint, gameSession)` 实现 IPrepaymentClient，位于 HTTP 包，绑定一个游戏会话。它向 `{ordersEndpoint}/{orderId}/prepare` 发 `{}`，只允许无凭据/查询/片段的 HTTPS 地址，Bearer 会话仅放请求头；15 秒超时、禁止重定向、响应流上限 64 KiB。换账号/会话时创建新实例，Dispose 中断在途请求并抑制回调；宿主必须保持激活且调用在主线程。

响应策略验证返回的游戏订单号与请求一致，平台单号非空且长度/控制字符合法；预支付不确定、冲突、凭证不可用和令牌拒绝按允许的状态码/错误码组合识别，未知正文不透传。服务器调用号经 GUID 校验后保存。连接失败保留 PAY.PREPAYMENT_UNCERTAIN，不自动重发下单；响应过大也不能推断平台未创建订单。

客户端已完成公共策略测试、真实 Unity 引用的独立程序集编译及独立工程 Play Mode 的失活/禁用/销毁宿主与释放后调用检查（SDK_HTTP_HOST_PLAY_PASS）。尚未进行在途请求释放、Unity HTTPS 成功/失败响应联调和平台真机验证。

`PreparedPaymentLauncher` 实现 IPaymentLauncher，拥有 IPrepaymentClient 与 IPlatformPaymentGateway。前者绑定当前游戏会话，通过 Prepare(orderId, callback) 返回 PrepaymentResult；返回订单号必须与输入一致，平台单号必须合法，才允许调用网关。重复预支付回调和重复终态均忽略，在途新调用返回 PAY.IN_PROGRESS，释放后抑制回调并释放依赖。两个接口的调用和回调均须在 Unity 主线程，HTTP 接入层负责派发。

它可以传给 VerifiedPayment。网关返回 RequiresServerQuery=true 时，编排查询游戏服务器订单状态，不调用 Confirm。Created 或 Paid 会继续有限轮询，Fulfilled 才成功；次数耗尽时分别返回 PAY.NOT_PAID 或 PAY.FULFILLMENT_PENDING。令牌加密托管、HTTPS 预支付客户端与受认证服务端入口已具备，平台工厂装配和真实支付联调仍待完成。

默认最多 12 次核验，间隔 2000 毫秒，第一次立即执行；构造参数 maxQueries（1–60）、intervalMilliseconds（100–60000）可调整。第一次若使用 Confirm 也计入次数，之后只 Get，不重新拉起收银台。由 SDK.Tick（UnitySdkHost 自动驱动）推进；直接使用 VerifiedPayment 的调用者须自行在主线程调用 Tick。重复回调不会增加轮询，最多一个查询在途；响应错误直接返回，平台取消且服务器仍为 Created 时保留平台错误。初始创建返回已 Paid/Fulfilled 的恢复订单仍立即返回现有状态，不再打开收银台。

1. 使用游戏 session、productId、稳定 requestId 创建或恢复订单。
2. 订单为 Fulfilled：直接返回成功，不再打开收银台。
3. 订单为 Paid：返回 PAY.FULFILLMENT_PENDING 和订单快照，不再打开收银台。
4. 订单为 Created：调用 IPaymentLauncher.Launch。
5. 平台返回回执时交 IOrderClient.Confirm；AwaitServerConfirmation 类型改用 Get 查询，成功必须以服务端 Fulfilled 为准。
6. 平台取消、失败或抛异常时，先 Get 查询服务端状态，处理客户端取消与服务端支付完成同时发生的情况。

不直接发放道具，不自动重复付款。有限轮询结束后仍为 Paid 或网络结果不确定时，保留 requestId 与 orderId，通过查询或同一个 requestId 恢复；禁止把重试变成新购买。

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

TikTokProviderFactory 已实现 IPayProviderFactory，可通过可选 createPayment 参数提供支付构造函数。构造函数只在 PayEnabled=true 时执行；未传入或返回 null 会拒绝初始化，不会退回 Mock。以下示例在官方平台初始化完成、游戏会话已取得后执行，host 为存活的 MonoBehaviour，ordersEndpoint 为 HTTPS 的 `/v1/orders` 地址：

```csharp
var factory = new ErDon.UnitySDK.TikTok.TikTokProviderFactory(
    "game-id", "test", createPayment: post => {
        var orders = new UnityOrderClient(host, ordersEndpoint);
        IPrepaymentClient preparation = null;
        IPaymentLauncher launcher = null;
        try {
            preparation = new UnityPrepaymentClient(host, ordersEndpoint, gameSession);
            launcher = new PreparedPaymentLauncher(preparation,
                new ErDon.UnitySDK.TikTok.TikTokPaymentGateway());
            return new VerifiedPayment(orders, launcher, post);
        } catch {
            orders.Dispose();
            if (launcher != null) launcher.Dispose();
            else if (preparation != null) preparation.Dispose();
            throw;
        }
    });
SDK.InitializeWithFactory(new SdkConfig {
    Platform = "TikTok", LoginEnabled = false, PayEnabled = true
}, factory, mainThreadPost, engineLog);
// 每帧主线程 SDK.Tick()；也可通过既有 Unity 宿主启动流程提供此工厂。
// SDK.Pay 的 PaymentRequest.Session 必须使用与 preparation 相同的 gameSession。
```

此示例是会话已存在时的显式装配，不是自动注册或面板一键完成登录再支付。更换账号或会话后需释放并重新装配，不能沿用旧会话绑定的 UnityPrepaymentClient。SDK 拥有成功创建的支付实现及其依赖；每次工厂调用必须创建全新实例。此处后端为真实游戏服务，不能直接把 TikTok 的平台 access_token 作为 gameSession。

若需先初始化、登录后再付款，推荐使用 PreparedPaymentLauncher 的工厂重载，避免捕获初始化时的会话：

```csharp
var factory = new ErDon.UnitySDK.TikTok.TikTokProviderFactory(
    "game-id", "test", createSessionExchange: createSessionExchange,
    createPayment: post => {
        var orders = new UnityOrderClient(host, ordersEndpoint);
        IPaymentLauncher launcher = null;
        try {
            launcher = new PreparedPaymentLauncher(
                session => new UnityPrepaymentClient(host, ordersEndpoint, session),
                new ErDon.UnitySDK.TikTok.TikTokPaymentGateway());
            return new VerifiedPayment(orders, launcher, post);
        } catch {
            orders.Dispose();
            if (launcher != null) launcher.Dispose();
            throw;
        }
    });
```

工厂重载实现 ISessionPaymentLauncher。VerifiedPayment 将本次 PaymentRequest 传给它；每次实际发起预支付时使用 request.Session 创建全新客户端，流程终态或 Dispose 释放它。没有请求/会话、商品不匹配会拒绝，不会沿用上一次会话。单次流程内会话固定，换账号后的新 PaymentRequest 使用新的会话；上层仍须管理退出登录时取消旧流程及清除旧会话。直接调用此启动器应使用 Launch(request, order, callback)，旧的 Launch(order, callback) 在动态模式会返回 PAY.INVALID_REQUEST。固定客户端构造方式仍兼容。

### 配置启动注入支付

`SdkPlatformRegistry.CreateStartup` 与 `UnitySdkHost.BeginConfiguredStartup` 新增可选 `createPayment` 参数，类型为 `Func<Action<Action>, IPay>`；可传入上面的动态会话组合构造函数。TikTok 自动注册声明支持显式支付装配且要求该工厂。配置勾选 payments 但未提供时，在创建平台运行时前返回 CONFIG.PAYMENT_PROVIDER_REQUIRED；关闭支付时忽略工厂，不创建支付客户端或网关。微信/抖音尚未声明此支持，不能通过传入工厂绕过 CONFIG.PAYMENT_UNIMPLEMENTED。

支付工厂在平台就绪后才调用，返回对象由 SDK 持有；可与 createNetwork 同时使用，平台身份保持不变。PAYMENTS 编译裁剪时不包含包装实现，运行时配置仍须与编译模块匹配。

Editor 面板通过 `ErDon.UnitySDK.Editor.EditorPlatformBootstrap.PaymentFactory` 接收同类型委托，显示“支付后端工厂：已注入/未注入”。在 Play Mode 中设置此属性，再勾选支付并点击“按配置启动平台 SDK”，即可走同一配置启动流程。不要在面板代码中保存会话、密钥或已经创建的 provider；注入构造函数即可。退出 Play Mode 自动清除工厂引用。面板不自动推测后端地址，不会主动拉起收银台；支付预览仍使用独立 Mock 区域。新增状态行尚未做逐项视觉验收，启动和清理已通过实际 Play Mode 验证。

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

SDK.Pay 对未初始化、模块关闭、缺少 provider、非法请求及并发调用返回明确错误；回调异常被隔离。超时由 SDK.Tick/Unity Update 驱动。VerifiedPayment 实现 IPaymentWait，SDK 取消/超时会停止其后续轮询并抑制迟到响应；释放也停止轮询。已经发出的 HTTP 请求和平台收银台未必被终止，底层启动器仍可能返回 PAY.IN_PROGRESS。取消本地等待不撤销交易，恢复必须使用原 requestId/orderId，不得用新编号重复扣款。其他自定义 IPay 可选择实现 IPaymentWait；未实现者维持原有本地回调取消行为。

Tools → ErDon SDK → 配置与预览：勾选支付，选择支付模拟结果，再初始化 Mock。可预览 Fulfilled、Pending、Cancelled、Timeout；按钮不创建真实订单。修改开关或模拟结果后需重新初始化。生产 Mock 构建继续阻断，真实支付平台亦未放行。


### TikTok 按需刷新与并发边界（2026-11-24）

TikTokApplication 已装配 TikTokCredentials。支付预下单或查单取用访问令牌时，若令牌过期或剩余不超过 60 秒，使用加密保存的 refresh_token 调用官方 OAuth 刷新接口；校验 open_id、Bearer 类型及有效期后，在同一 SQLite 事务中写入两种新令牌。没有刷新令牌时保留原访问令牌的有效性判断，过期不会伪造刷新成功。

同一管理器合并同用户并发刷新。存储以旧刷新记录的随机 revision 做条件更新：新登录或 remove 已替换/删除记录时，旧刷新结果不能写回；调用方重新读取当前凭据。两类令牌使用不同 AES-GCM AAD，不能交换密文用途。上游错误统一映射 PAY.CREDENTIAL_UNAVAILABLE，不泄露 token 或错误正文。

当前参考服务按单进程运行。revision 防止跨进程旧写入，但不提供跨进程上游刷新互斥；部署多副本前必须增加共享刷新租约或集中凭据服务，避免两个进程同时轮换同一个 refresh_token。平台撤销传输方法 createTikTokTokenClient().revoke 已实现和测试，但尚未公开授权撤销路由，也未完成游戏会话失效联动；SDK.CancelLogin 不等于撤销授权。

验证覆盖令牌轮换、访问令牌过期后的刷新、并发合并、新登录/删除与迟到响应竞争、密文用途校验，以及真实本机 HTTP 服务中的登录→刷新→预下单→重启后查单。平台响应使用测试替身，尚未进行真实 TikTok 授权联调。
