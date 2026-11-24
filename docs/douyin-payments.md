# 抖音道具直购接入

上层仍使用 `SDK.Pay`。本次实现道具直购：服务端生成受认证的订单参数，Unity 调起官方收银台，服务端验签通知并核对订单后标记 Paid，由发货工作器推进到 Fulfilled。只有 Fulfilled 才向上层返回支付成功。游戏币充值、扣币、退款和主动平台查单暂未接入。

## Unity 装配

安装核心、`com.erdon.unitysdk.transport.http`、`com.erdon.unitysdk.douyin` 和已验证的官方 TTSDK，启用抖音平台宏。面板勾选登录、支付，应用构建能力选择。以下代码放入持久存在的 MonoBehaviour；将 `this` 用作 HTTP 协程宿主。服务地址替换为自己的 HTTPS 服务，不能将 App Secret 或回调 Token 放在客户端。

```csharp
using System;
using ErDon.UnitySDK;
using ErDon.UnitySDK.Douyin;

// 在组件方法内执行，且早于 SDK.Pay。
var route = DouyinPaymentRoute.Android; // iOS 工程显式选择 IosCustomerService
var ordersUrl = "https://game.example.com/v1/orders";
var config = new SdkRuntimeConfiguration {
    platform = "Douyin", gameId = "my-game", appId = "YOUR_APP_ID",
    production = false, login = true, payments = true
};
Func<Action<Action>, IPay> paymentFactory = dispatch => new VerifiedPayment(
    new UnityOrderClient(this, ordersUrl),
    new PreparedPaymentLauncher<DouyinPaymentRequest>(
        session => new UnityPreparedRequestClient<DouyinPaymentRequest>(
            this, ordersUrl, session,
            json => DouyinPaymentPreparation.Parse(json, route)),
        new DouyinPaymentGateway(route),
        DouyinPaymentPreparation.MatchesOrder),
    dispatch);

UnitySdkHost.BeginConfiguredStartup(config,
    () => new UnitySessionExchange(this, "https://game.example.com/v1/session",
        config.gameId, config.appId, config.Environment),
    result => { /* 检查启动结果，再登录 */ },
    createPayment: paymentFactory);
```

后续调用：

```csharp
// gameSession 是 SDK.Login 经游戏服务器验证后返回的会话。
// purchaseRequestId 在一次购买及其恢复过程中保持不变。
SDK.Pay(new PaymentRequest(gameSession, "sword", purchaseRequestId), result => {
    if (result.Success) {
        // 刷新游戏服务器库存。不要在本地再次发道具。
    } else {
        // 保存订单与请求 ID，根据 result.Error.Code 展示处理中或错误原因。
    }
});
```

预支付工厂每次收到当前 PaymentRequest 的 Session，切换账号不会复用旧客户端。请求只提交订单 ID，客户端不能通过正文改价。`DouyinPaymentPreparation` 验证响应路由、道具直购字段和订单 ID；启动器再次核对订单金额和 CNY 价格单位。iOS 路由使用钻石作为平台支付币种，游戏商品价格仍按人民币分保存；服务器与客户端路由必须一致。

支付开关关闭时不创建支付实例。开启但没有注入工厂时，配置启动返回 `CONFIG.PAYMENT_PROVIDER_REQUIRED`；不会用 Mock 代替。Editor 面板可把同一工厂赋给 `EditorPlatformBootstrap.PaymentFactory`，该入口不会自动购买。关闭支付的 Player 编译会排除启动器、HTTP 实现与平台支付实现，基础契约保留。

## 服务端装配

Node.js 24.20+，使用 `createDouyinApplication`。示意代码位于 server 目录运行；两个数据库文件的父目录必须已存在，密钥来自部署环境。监听器仅绑定本机，由 HTTPS 反向代理转发。必须配置真实游戏的 `deliver`，本示例不以空函数假装发货。

```javascript
import { createServer } from 'node:http';
import { createDouyinApplication } from './src/douyin-application.mjs';
import { deliver } from './your-game-delivery.mjs'; // 游戏自己实现的幂等入库事务

const app = createDouyinApplication({
  binding: { gameId: 'my-game', appId: process.env.DOUYIN_APP_ID,
    environment: 'test', platform: 'Douyin', secret: process.env.DOUYIN_APP_SECRET },
  callbackToken: process.env.DOUYIN_CALLBACK_TOKEN,
  authPath: './data/auth.sqlite', orderPath: './data/orders.sqlite',
  route: 'Android', zoneId: '1',
  catalog: [{ productId: 'sword', productName: '宝剑', amountMinor: 600, currency: 'CNY' }],
  deliver
});
const server = createServer({ requestTimeout: 10000, headersTimeout: 5000, maxHeaderSize: 8192 }, app.handler);
server.listen(8788, '127.0.0.1');
// 停止时先排空 HTTP 请求，再 await app.close()；close 会等待在途发货完成。
```

`deliver(message)` 的输入包含 deliveryKey、subject、productId、gameId、environment。游戏数据库必须在同一事务内按 deliveryKey 去重并发道具，成功后返回 true。调用失败时保持 Paid，定时工作器之后重试，不能仅依赖进程内去重。

开发者后台配置服务端通知地址为 `https://game.example.com/v1/webhooks/douyin/payment`，回调 Token 与服务器一致。此路径支持签名 GET 挑战和 JSON POST 支付通知。`environment: 'test'` 仅用于 SDK 数据隔离，不会把官方收银台切换为沙盒，不能据此判断不会扣款。

| 接口 | 认证与行为 |
| --- | --- |
| POST /v1/session | 平台 code 换游戏会话；平台密钥不返回客户端 |
| POST /v1/orders | Bearer 游戏会话；只接收 productId、requestId，价格来自目录 |
| POST /v1/orders/{id}/prepare | Bearer 会话；持久化订单、账号、价格、路由及随机 extraInfo；重复请求返回相同参数 |
| GET /v1/orders/{id} | Bearer 会话；返回游戏订单状态，SDK 用于轮询 |
| POST /v1/orders/{id}/confirm | 客户端证据不能将 Created 改为 Paid |
| GET/POST /v1/webhooks/douyin/payment | 平台签名验证；提交 Paid 事务后才返回 200 |

## 通知核验与错误

通知按官方 SHA-1 排序协议验签，使用原始 `msg` 字符串，不能解析后重排再计算。随后核对 App ID、订单绑定、随机 extraInfo、金额和路由币种。回调没有玩家 openid，接收方只能关联到预先认证并持久化的游戏订单，不能声称额外验证了付款人的平台身份。extraInfo 是关联字段，不是可独立证明支付成功的凭据。

通知重试通过持久化订单/交易号去重，已发货订单不会降级为 Paid。未施加自定的短时间戳失效窗口，以免拒绝平台延迟重试；应用迁移时必须保留订单及交易幂等记录，不能清库后重用旧订单号。订单金额单位与币种规则依据[官方小游戏回调文档](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/api/javascript-api/payment/payment-server-callback)，不套用抖音小程序担保支付协议。

| 错误码 | 原因与处理 |
| --- | --- |
| AUTH.SESSION_INVALID | 会话失效；重新登录，再恢复原订单 |
| PAY.PRODUCT_CHANGED | 未预支付订单的商品目录发生变化；先核对原订单，不能直接自动重下单 |
| PAY.INVALID_RESPONSE | 返回参数缺失、路由/订单/金额不匹配；收银台不会启动 |
| PAY.ORDER_ALREADY_PAID | 已 Paid/Fulfilled，不再生成支付参数；查询原订单 |
| WEBHOOK.INVALID_SIGNATURE | 通知签名错误；核查后台回调 Token，不记录原始签名或正文 |
| WEBHOOK.INVALID_PAYLOAD | 已验签正文不符合协议或应用不匹配 |
| PAY.VERIFICATION_MISMATCH | 通知金额、币种或关联字段不匹配；不入账 |
| PAY.TRANSACTION_REUSED / PAY.TRANSACTION_CONFLICT | 同一平台交易被关联到不同订单，或订单交易号变化；人工对账 |

## 验证范围

已通过 Node 本机 HTTP 登录→预支付→签名通知→发货工作器测试、重启/重复通知/篡价/跨账号测试；Unity 官方 TTSDK 6.9.7 编译及注入回调、类型化参数与工厂测试；新增 HTTP 客户端在隔离 Unity 中的宿主失效/释放检查。以上不代表真实支付、iOS 兑换规则、平台主动查单或公网部署已验收。客户端丢失通知后的平台主动查单仍待实现，当前保留 Created，不能从收银台成功回调推断到账。
