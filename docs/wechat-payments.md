# 微信小游戏道具直购

上层统一调用 `SDK.Pay`。服务端负责订单、签名和凭据，Unity 只使用服务器签好的原串调起 `wx.requestMidasPaymentGameItem`。收银台回调不代表发货成功；游戏服务器确认 `Fulfilled` 才算成功。

范围是微信小游戏虚拟支付 2.0 的 Android 道具直购、CNY、每单一件、原价支付。商城下单、代币、折扣和退款不是此接口的功能。请勿把小程序 `xpay` 协议替换进来。

## 配置与部署

复制 `server/config/wechat-payment.example.json` 为部署配置。填写游戏绑定、微信支付 2.0 的 `offerId`、`zoneId`、小游戏原始 ID `gh_...`、环境和商品目录。价格是人民币分，必须与微信平台商品配置一致。订单库会持久化 SDK UUID 与 32 字符微信业务订单号的映射。

凭据通过环境变量提供：

| 变量 | 用途 |
|---|---|
| `WECHAT_APP_ID` / `WECHAT_APP_SECRET` | 服务端 code2Session 和稳定应用令牌 |
| `WECHAT_PAYMENT_APP_KEY` | 当前支付环境对应的支付 AppKey，不是 AppSecret |
| `WECHAT_CALLBACK_TOKEN` | 微信支付发货推送配置中的 Token |
| `WECHAT_CREDENTIAL_KEY_HEX` | 自行生成的 32 字节 AES 密钥，64 位十六进制；跨重启保留 |

运行 `node server/src/config-cli.mjs check <配置路径>` 先看缺失变量。然后按照 [服务端配置](server-configuration.md) 启动，并配置 `SDK_DELIVERY_MODULE` 指向真实游戏的幂等发货模块。回调入口是 `https://你的域名/v1/webhooks/wechat/payment`，微信平台选择 **JSON、明文模式**；HTTPS 必须由反向代理提供。当前入口明确拒绝 XML/AES 模式，不会把未知加密格式误认为成功。

GET 校验 Token、时间戳和 nonce 的 SHA-1 签名后返回 echostr；POST 另外校验 `Event + '&' + Payload` 的 AppKey HMAC-SHA256。核对原始 ID、OpenId、环境、商品、数量、金额、分区、透传随机值和交易号后才标记 Paid。平台测试通知只返回测试成功，不变更订单。正式通知在游戏完成幂等发货后返回 `ErrCode:0`；发货失败保留 Paid、返回失败并允许工作器或平台重试。

登录返回的 session_key 仅存于 AES-256-GCM 凭据库，不返回 Unity。默认本地保留上限为两小时，这不是微信公布的有效期；微信实际有效期不可预测。新登录会替换密钥。多进程同时登录同一个账号时不要依赖旧 session_key 继续支付；遇到 `PAY.SESSION_EXPIRED` 重新登录并准备订单。

## Unity 装配示例

安装核心、HTTP 和微信适配包及官方微信 SDK。面板选微信、登录、支付，并应用构建配置。在持久 MonoBehaviour 的方法中设置：

```csharp
using System;
using ErDon.UnitySDK;
using ErDon.UnitySDK.WeChat;

var ordersUrl = "https://game.example.com/v1/orders";
var config = new SdkRuntimeConfiguration {
    platform = "WeChat", gameId = "your-game", appId = "YOUR_APP_ID",
    production = false, login = true, payments = true
};
Func<Action<Action>, IPay> paymentFactory = dispatch => new VerifiedPayment(
    new UnityOrderClient(this, ordersUrl),
    new PreparedPaymentLauncher<WeChatPaymentRequest>(
        session => new UnityPreparedRequestClient<WeChatPaymentRequest>(
            this, ordersUrl, session, WeChatPaymentPreparation.Parse),
        new WeChatPaymentGateway(), WeChatPaymentPreparation.MatchesOrder),
    dispatch);
UnitySdkHost.BeginConfiguredStartup(config,
    () => new UnitySessionExchange(this, "https://game.example.com/v1/session",
        config.gameId, config.appId, config.Environment),
    result => { /* 启动成功后登录，保存游戏会话 */ },
    createPayment: paymentFactory);
// 在登录完成后的购买按钮中调用；一次购买及恢复使用同一个 requestId。
SDK.Pay(new PaymentRequest(gameSession, "your-platform-product-id", requestId), result => {
    // 成功后刷新服务器库存，不要在客户端再次发货。
});
```

`WeChatPaymentPreparation` 保留 signData 原串，验证订单、金额和商品再启动支付。切换会话会创建新的预支付客户端。关闭支付模块会裁剪实现；不需要在 WebGL 启用线程。

## 查单、错误与恢复

`POST /v1/orders/{id}/query` 需要 Bearer 游戏会话。服务器用稳定令牌普通模式、session_key 和 AppKey 调用 `pay_v2.queryOrder`；返回 `platformStatus` 和本地状态。平台查单没有金额及付款人完整证据，因此不会单凭 `paid` 发货。以已验证发货通知和游戏库存为准。通知缺失时使用[管理员恢复](operator-recovery.md)核查和补账。

| 错误码 | 原因及处理 |
|---|---|
| `PAY.SESSION_EXPIRED` | 密钥未保存、本地保留过期，或平台返回 90016；重新登录 |
| `PAY.CONFIG_CHANGED` | 已创建订单的 offer/分区/环境与当前部署不同；恢复原配置核对订单 |
| `PAY.PRODUCT_CHANGED` | 订单创建后商品价格改变；核对商品目录 |
| `PAY.VERIFICATION_MISMATCH` | 通知与预订单的身份、金额或商品不符；不要发货 |
| `WEBHOOK.INVALID_SIGNATURE` | 回调 Token 或支付 AppKey 不匹配，或正文被改动 |
| `PAY.TRANSACTION_REUSED` / `PAY.TRANSACTION_CONFLICT` | 交易号跨订单复用或同订单冲突；保留证据排查 |
| `PAY.QUERY_UNAVAILABLE` / `PAY.QUERY_RESPONSE_INVALID` | 平台超时、格式异常或拒绝；核对服务端白名单和凭据 |
| `PAY.DELIVERY_PENDING` / `PAY.DELIVERY_FAILED` | 已付款但游戏未持久发货；修复发货模块后重试 |

数据库备份必须包含认证、订单、凭据三库与部署密钥。密钥轮换时 `keysEnv` 同时保留旧/新 key ID，将 `currentKeyId` 指向新密钥；旧行仍需旧密钥解密，不能提前移除。修改支付环境或 AppKey 应先处理旧订单，不能把测试订单迁移成正式订单。

协议来源：[收银台](https://developers.weixin.qq.com/minigame/dev/api/midas-payment/wx.requestMidasPaymentGameItem.html)、[直购与发货](https://developers.weixin.qq.com/minigame/dev/guide/open-ability/virtual-payment/goods.html)、[通知签名](https://docs.qq.com/doc/DVVZZdHFsYkttYmxl)、[查单](https://developers.weixin.qq.com/minigame/dev/api-backend/midas-payment/order/api_pay_v2.queryorder)、[稳定令牌](https://developers.weixin.qq.com/minigame/dev/api-backend/access-token/api_getstableaccesstoken.html)。本地测试覆盖模拟上游与官方签名向量；真实平台配置、真实交易与真机验收需另外执行。
