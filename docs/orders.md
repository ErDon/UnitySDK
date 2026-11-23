# 服务端订单与发货（开发中）

server/src/orders.mjs 提供 OrderService 与 MemoryOrderStore。当前是可执行的服务组件，不是可上线支付服务；尚无支付 HTTP 路由、平台下单/验签/查单实现、持久化订单、退款或对账。不会从客户端 paid=true 直接发货。

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
