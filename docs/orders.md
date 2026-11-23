# 服务端订单与发货（开发中）

server/src/orders.mjs 提供 OrderService 与 MemoryOrderStore。当前是可执行的服务组件，不是可上线支付服务；已有可装配订单 HTTP 处理器，但尚无平台下单/验签/查单实现、生产持久化验收、退款或对账。不会从客户端 paid=true 直接发货。

## 装配

OrderService 构造参数：binding（服务端受信的 gameId/appId/environment/platform）、catalog（productId/amountMinor/currency）、auth（现有 AuthService）、store、verifyPayment。verifyPayment 是必须的服务端适配器，不提供默认成功实现；不得把客户端回执直接作为其返回值。

金额使用正安全整数的货币最小单位，currency 为三个大写字母；不同平台的币种和价格规则须在正式接入时验证。客户端只能选择 productId，提交的价格或身份字段不影响订单。catalog 是构造时的快照。

## 接口

| 接口 | 输入与结果 |
| --- | --- |
| create(session, {productId, requestId}) | 校验游戏会话，按服务端商品表创建订单；返回公开订单字段 |
| get(session, orderId) | 只允许相同绑定和账号读取，其他账号与不存在订单统一返回 NOT_FOUND |
| confirm(session, orderId, evidence) | evidence 交给可信平台验证器；核对已支付、订单、应用、平台、账号、金额、币种和交易号后才标记 Paid |
| fulfill(orderId, deliver) | 仅供服务端工作进程；Paid 时调用游戏发货处理器，明确返回 true 后标记 Fulfilled |
| store.pendingDelivery() | 服务端扫描 Paid 订单，安排重试；不可暴露为客户端接口 |

公开字段为 orderId/productId/amountMinor/currency/state/createdAt，不包含账号、原始回执或平台令牌。状态 Created → Paid → Fulfilled。confirm 不自动发货；重复确认或发货保持已有状态。

同一个 requestId 按绑定与账号隔离：相同商品返回原订单，换商品返回 PAY.IDEMPOTENCY_CONFLICT。订单保留创建时价格，后续调价不回写。交易号按 platform/appId 归属唯一订单，不因不同测试环境而允许重复使用。

## 平台验证器契约

verifyPayment(binding, order, evidence) 异步返回：paid、orderId、appId、platform、subject、amountMinor、currency、transactionId。order 为只读快照。适配器必须先完成官方签名/可信查单和环境校验，不能信任客户端声明，也不能仅检查字符串格式。缺字段、归属或金额不符拒绝；抛错转换为 PAY.VERIFICATION_FAILED，不泄露上游异常。

当前没有真实平台验证器，因此不能以此组件宣称微信、抖音或 TikTok 支付可用。

## 发货契约及重试

游戏 deliver 接收 {deliveryKey, subject, productId, gameId, environment}。deliveryKey 永远等于 orderId；游戏服务必须把去重记录和道具发放放入同一数据库事务，重复编号返回已完成。不要用客户端存档承担幂等性。

游戏已经发货但响应丢失时，订单保留 Paid，重试同一 deliveryKey；游戏去重后确认，订单再变为 Fulfilled。单个 OrderService 实例阻止同时发同一订单，但多实例仍依赖持久化存储及接收端事务，不能宣称分布式 exactly-once。

## 存储与错误

MemoryOrderStore 有默认一万订单容量上限，满后返回 SERVER.CAPACITY，不删除未结算订单腾空间。进程重启会丢失全部记录，只用于本地验证。生产存储需原子完成：幂等键与订单创建、支付交易唯一占用与 Paid 状态写入、发货完成状态写入；Paid 必须可持久扫描恢复。

主要错误：PAY.INVALID_REQUEST、PAY.PRODUCT_NOT_FOUND、PAY.ORDER_NOT_FOUND、PAY.IDEMPOTENCY_CONFLICT、PAY.NOT_PAID、PAY.VERIFICATION_FAILED、PAY.VERIFICATION_MISMATCH、PAY.TRANSACTION_REUSED、PAY.TRANSACTION_CONFLICT、PAY.DELIVERY_IN_PROGRESS、PAY.DELIVERY_FAILED。当前没有自动重试后台；游戏发货 worker 应按错误及幂等键安排重试，不能重发未验证支付的订单。

测试使用模拟支付验证器及真实 AuthService 组合，覆盖客户端篡价、账号/环境隔离、会话撤销、重复下单、并发确认、交易复用、发货响应丢失和容量边界。真实平台、数据库事务和端到端支付仍未验收。


## SQLite 持久化实现

server/src/sqlite-orders.mjs 提供 SqliteOrderStore，可替换 MemoryOrderStore 传入同一个 OrderService。Node 24.20.0 基线，使用内置 node:sqlite；需要已存在、可写的本地目录。

```javascript
import { SqliteOrderStore } from '../server/src/sqlite-orders.mjs';
const store = new SqliteOrderStore('./data/orders.sqlite', { capacity: 10000 });
// 将 store 传给 OrderService；完成所有工作后 store.close()。
```

订单与 requestKey 使用同一事务插入。交易号使用唯一索引，BEGIN IMMEDIATE 将校验归属与标记 Paid 放在同一写事务中；Paid 是持久待发货集合。重启后扫描 pendingDelivery，再使用同一个 orderId 作为 deliveryKey 重试。标记 Fulfilled 不删除交易号占用或幂等键，防止旧请求被再次消费。

WAL + FULL 同步提交；数据库表有状态与交易号一致性约束。schema 版本当前为 1，未知版本拒绝启动，不擅自迁移。关闭后访问或底层存储失败返回 SERVER.STORAGE_UNAVAILABLE，不输出 SQL、路径或账号正文。容量满不清除已结算订单，需后续受控归档策略。

当前支持同机数据库连接，不支持网络文件系统或多机共享 WAL。同步 SQLite 会阻塞 Node 事件循环；高负载性能、备份恢复和故障注入尚未验收。订单数据库含账号信息，应限制权限；备份须按 SQLite 的在线备份/WAL 规则进行。游戏端持久去重仍必不可少，此存储不将远端发货变成跨服务事务。

5 项 SQLite 订单测试验证数据库重开、幂等状态、回滚、容量、四个独立 worker 抢同一交易号、未知 schema 拒绝以及发货响应丢失后重启恢复。未验证真实支付平台。


## HTTP 处理器

createOrderHandler(orderService, options) 位于 server/src/orders-http.mjs，可作为 Node HTTP request listener。必须显式装配已配置可信 verifyPayment 的 OrderService；默认 start.mjs 仍仅启动登录服务，不偷偷启用测试支付验证器。部署需 HTTPS、域名和受信代理配置。

所有请求使用 Authorization: Bearer <游戏会话>，不接受 query token。绑定由服务端 OrderService 确定，不能通过请求切换游戏、平台或环境。

| 方法与路径 | JSON 请求 | 结果 |
| --- | --- | --- |
| POST /v1/orders | {"productId":"coins","requestId":"purchase-001"} | 创建或返回相同请求的订单 |
| GET /v1/orders/{orderId} | 无 | 当前账号的公开订单 |
| POST /v1/orders/{orderId}/confirm | {"evidence":{...}} | 交平台验证器核验后更新支付状态 |

响应成功统一 HTTP 200 {order,callId}，失败 {errorCode,callId}。callId 是服务端 UUID，与 SDK 日志关联；所有响应 no-store。订单号为服务端生成的小写 UUID，路径不接受查询参数或其他方法。没有 /fulfill 客户端路由。

默认每来源 60 秒 30 次、JSON 正文最多 8192 字节，超限分别为 PAY.RATE_LIMITED/429（含 Retry-After）与 REQUEST.TOO_LARGE/413。不信任 X-Forwarded-For，代理后的来源额度需按部署环境配置 limiter。非 JSON 为 415，错误格式为 400，未知路径为 404，无效会话为 401，跨账号订单与不存在订单统一 404。超过正文上限或提前拒绝时关闭连接，不复用带未读正文的连接。

options.log 接收服务端已格式化 JSON，包含 time/interface/callId/logId/stage/params/result；订单正文、授权头和回执整体脱敏，日志回调异常不改变响应。测试中的 evidence.valid 只存在测试验证器中，不能用于真实服务。


## Unity 订单通信层

核心提供 IOrderClient、OrderResult、GameOrder、OrderState；可选 HTTP 包提供 UnityOrderClient。它是支付编排的下层接口，已由 VerifiedPayment 接入 SDK.Pay，真实平台支付参数仍待接入，也不直接启动平台收银台或发货。

```csharp
// this 是存活、启用的 MonoBehaviour；在主线程创建和调用。
IOrderClient orders = new UnityOrderClient(this, "https://your-game.example/v1/orders");
orders.Create(gameSession, "coins", purchaseRequestId, result => {
    if (!result.Success) {
        UnityEngine.Debug.LogWarning(result.Error.Code + " serverCallId=" + result.ServerCallId);
        return;
    }
    // result.Success 只是操作成功，Created 不代表已付款。
    string orderId = result.Order.OrderId;
    orders.Get(gameSession, orderId, latest => {
        if (latest.Success && latest.Order.State == OrderState.Fulfilled) {
            // 向游戏服务同步已发放的权益，不能在这里再发一次。
        }
    });
});
// 平台回执是字符串时：orders.Confirm(gameSession, orderId, receipt, callback);
// 停止使用时 orders.Dispose(); 中止本实例请求并不撤销服务端已执行的操作。
```

purchaseRequestId 应在一次购买开始时生成并保存，网络不确定时重试原值，不能每次重试都换号。订单价格完全由服务端返回。Confirm 的 evidence 当前是字符串字段，服务端平台验证器需支持对应字符串回执；结构化平台证据需另行扩展，不能把测试 evidence.valid 当作真实凭证。

请求只走 HTTPS，禁止 URL 凭据/query/fragment，禁用重定向。会话仅放 Authorization 头，不输出会话、回执或响应正文。15 秒超时，JSON 请求 UTF-8 最多 8192 字节；响应收到后检查 64 KiB（不是下载时流量限制）。Dispose 后不再派发在途结果。调用方负责 host 生命周期、结果回调异常与统一 SDK 层日志；当前下层客户端不自行输出日志、不自动重试。

结果策略核对订单 UUID、期望订单/商品、非空商品、正整数金额（不超过 JS 安全整数）、币种形状及三个已知状态。未知状态、矛盾响应、换单响应均为 PAY.INVALID_RESPONSE。服务端错误仅映射已登记 HTTP/错误码组合，其他返回 PAY.SERVER_REJECTED；429 提供 RetryAfterSeconds（仅接受正整数），401 会话错误提示重新登录。ServerCallId 只保留标准 UUID。

协议单元测试覆盖状态、换单、金额与错误字段；Unity 编译验证不等于 HTTPS 实机联调。SDK.Pay 聚合已接入，真实平台支付仍是后续工作。
