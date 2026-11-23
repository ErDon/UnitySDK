# 支付编排（开发中）

VerifiedPayment 实现 IPay，组合 IOrderClient 与 IPaymentLauncher。它负责客户端流程，不能替代服务端平台下单、验签与发货。当前尚未接 SDK.Pay 聚合、配置面板或真实平台收银台。

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

尚无编排级超时/取消入口；HTTP 传输有自身超时，平台若不回调需由未来聚合层控制等待。不能把 Dispose 视为取消已经进行的支付。上层聚合日志/调用编号尚未接入，下层不输出 session、回执或任意异常文本。

常见流程错误：PAY.INVALID_REQUEST、PAY.IN_PROGRESS、PAY.CREATE_FAILED、PAY.LAUNCH_FAILED、PAY.VERIFICATION_FAILED、PAY.INVALID_RESPONSE、PAY.NOT_PAID、PAY.FULFILLMENT_PENDING。Created 与确认结果必须保持相同订单/商品/金额/币种，未知状态或换单拒绝。服务端已完成发货优先于平台取消提示，查询失败不会伪造取消成功。

PaymentResult.Success 只有服务端订单状态为 Fulfilled 时成立。Order 可能为空（创建或网络失败），读取前检查。ServerCallId 来自服务端订单响应，不能将它和未来客户端调用编号混用。

## 验证范围

PaymentPipelineTests 覆盖重复回调、主线程队列、并发支付拒绝、平台成功后仍等待服务端、Paid/Fulfilled 恢复不再拉起收银台、取消竞争、订单被替换及释放失效。订单与 launcher 均使用测试实现，不等同于真实支付验收。
