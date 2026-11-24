# 统一结果与错误诊断

本文对应当前 C# 实现，不是平台官方错误码表。预期业务失败经结果.Error 返回；非法构造参数、生命周期重入或配置装配错误也可能抛异常，见各接口文档。尚未完成稳定版发布与真机全量映射验收。

## SdkError 的实际字段

| C# 字段 | 含义 |
| --- | --- |
| Code | 稳定的 SDK 错误分类，用于业务分支 |
| Reason | 面向开发者的已知原因；无法确定时不会推断 |
| PlatformCode | 平台返回的原始码字符串，可能为空 |
| PlatformMessage | 适配器提供的脱敏平台说明，可能为空；不是直接给玩家的文案 |
| Operation / Stage | 聚合接口与发生阶段，例如 SDK.Login、PlatformAuthorization |
| CallId / TraceId | 本次聚合调用关联 ID，当前 TraceId 等于 CallId |
| ServerCallId | 后端返回且经过校验的关联 ID，可能为空；与客户端 CallId 不保证相等 |
| Retry | Unknown、Never、UserAction、Delayed，默认 Unknown；只是重试建议，不自动重试 |
| RetryAfterSeconds | 可空秒数，仅在有明确证据时填入；空不等于零秒 |

没有 MessageKey、Message 或 Platform 属性。玩家提示与本地化由游戏按 Code 映射；平台身份由 SDK 配置及结构化日志提供。不要解析 Reason/PlatformMessage 的文本驱动业务，更不能据模糊提示判定封号、地区限制或恢复时间。

## 登录失败按阶段排查

| 错误码 | 已知原因与下一步 |
| --- | --- |
| SDK.NOT_INITIALIZED | 尚未完成 SDK 初始化；先等待启动结果 |
| MODULE.DISABLED | 登录模块未启用；修改配置并重新初始化 |
| CAPABILITY.NOT_SUPPORTED | 没有对应能力实例；检查工厂/手动初始化 |
| AUTH.IN_PROGRESS | 当前已有登录等待；避免重复触发 |
| AUTH.CANCELLED | 游戏调用 CancelLogin 结束本地等待；不等于用户在平台授权页取消 |
| AUTH.TIMEOUT | SDK 等待达到本地超时；不是平台返回的封禁/限流结论 |
| AUTH.INVALID_CREDENTIAL | 平台未返回有效临时凭证，或传入交换层的凭证不完整 |
| AUTH.NOT_LOGGED_IN | 抖音适配收到未登录状态；按平台流程登录 |
| AUTH.PLATFORM_FAILED | 平台授权失败；有原始码时查看 PlatformCode，不猜码含义 |
| AUTH.PLATFORM_EXCEPTION | 调用平台接口时抛异常，原文不输出 |
| AUTH.APP_NOT_ALLOWED | 后端不允许当前游戏、应用或环境组合；检查服务端绑定 |
| AUTH.CODE_REUSED | 临时登录凭证已提交过；获取新凭证，不重复使用旧 code |
| AUTH.PLATFORM_REJECTED | 服务端向平台兑换凭证被拒绝；重新授权或排查应用绑定 |
| AUTH.PLATFORM_CONFIGURATION | 平台拒绝服务端应用配置；由运营/开发检查密钥等配置 |
| AUTH.PLATFORM_UNAVAILABLE | 服务端无法连通平台；恢复后使用新的临时凭证 |
| AUTH.PLATFORM_RESPONSE_INVALID / AUTH.INVALID_IDENTITY | 上游响应或解析出的身份无效；检查验证器和官方版本 |
| AUTH.INVALID_SESSION | 返回会话为空或违反响应约定，不当作登录成功 |
| AUTH.INVALID_RESPONSE | 会话交换 HTTP 响应无法按协议解析 |
| AUTH.RATE_LIMITED | 后端明确限流；仅有有效 RetryAfterSeconds 时显示明确倒计时 |
| AUTH.SERVER_REJECTED | 后端拒绝，但状态码/错误码不属于允许的已知组合 |
| AUTH.UNKNOWN | 聚合/编排收到无法使用的登录结果 |

SessionResponsePolicy 只接受已知状态码/错误码组合，不回传任意服务器正文。HTTP 429 返回 Delayed；有效正整数 Retry-After 才写入 RetryAfterSeconds。平台调用没有提供具体原因时，必须保留未知。Mock 的 AUTH.USER_CANCELLED、AUTH.PERMISSION_DENIED 是可选模拟结果，不代表三平台生产适配已经完成这些码的精确映射。

## 上层处理示例

```csharp
using ErDon.UnitySDK;

SDK.Login(result => {
    if (result.Success) {
        // 将 result.Session 交给游戏会话管理，不打印到日志。
        return;
    }
    var error = result.Error;
    switch (error.Code) {
        case "AUTH.CANCELLED":
            // 关闭等待 UI，无需显示系统故障。
            break;
        case "AUTH.RATE_LIMITED":
            // 只有 error.RetryAfterSeconds.HasValue 才显示精确等待时间。
            break;
        default:
            // 按 Code 映射游戏文案；给客服提供 CallId/ServerCallId。
            break;
    }
});
```

该片段在 SDK 已初始化后调用。不能将每种失败都自动重试；尤其临时凭证、支付和上报可能产生重复操作。WithRetry 允许适配器设置策略，但调用它不会安排重试任务。

## 同步异常与业务失败的边界

调用方需要分别处理接入错误和业务结果。下面对应聚合入口的当前行为，不覆盖自定义 provider 的所有实现：

| 情况 | 交付方式 | 接入方处理 |
| --- | --- | --- |
| Login/Share/ShowRewardedAd/Pay/Request/异步 ReportEvent 的 callback 为 null | 直接抛 ArgumentNullException | 修复调用代码；没有结果回调 |
| Login/Share/ShowRewardedAd/Pay 的超时不大于零 | 直接抛 ArgumentOutOfRangeException | 调用前校验；异步 ReportEvent 还要求不超过 120000 毫秒 |
| 从日志回调、能力初始化/释放或受保护的同步 provider 调用中重入 SDK | 直接抛 InvalidOperationException | 调整调用顺序，不在这些回调里执行 SDK 操作 |
| 未初始化、模块关闭、能力缺失 | 业务结果中的 SDK.NOT_INITIALIZED、MODULE.DISABLED、CAPABILITY.NOT_SUPPORTED | 按结果处理；不应只依赖 try/catch |
| Share/Request/异步 ReportEvent 的请求对象为 null | 通过结果返回对应 INVALID_ARGUMENT；前置状态错误优先 | 修复参数；构造请求对象自身也可能先抛参数异常 |
| 游戏提供的完成回调自己抛异常 | SDK 记录 CALLBACK.EXCEPTION，不重新调用回调 | 排查游戏回调；不能据此重新执行付款等操作 |

前置拒绝可能在调用返回前同步执行完成回调。应先设置等待 UI，再调用 SDK；不要在调用之后无条件把界面改回“等待”。网络和上报的回调也不能假设返回的 operation 已赋值。关闭 SDK 后旧业务回调会被抑制，生命周期负责人自行清理 UI。

同步参数/重入异常发生时，不能依赖 SDK 已生成 CallId 或成对日志。初始化配置异常的处理见下节；不要把异常统一翻译成“平台登录失败”，也不要打印可能含敏感信息的任意异常正文。

## 启动、配置与生命周期

初始化错误使用 INIT 前缀，例如 INIT.TIMEOUT、INIT.NOT_READY、INIT.COMPOSITION_FAILED、INIT.CANCELLED。INIT.PLATFORM_FAILED 可带平台原始初始化码；微信当前核对的成功码是200，抖音/TikTok 是0，不能跨平台共用成功码判断。

配置验证返回 CONFIG.* 问题列表；ToSdkConfig/注册/装配可能抛 InvalidOperationException，并不是所有错误都包装成业务结果。常见有 CONFIG.APP_ID_REQUIRED、CONFIG.GAME_ID_REQUIRED、CONFIG.PLATFORM_MISMATCH、CONFIG.PLATFORM_NOT_REGISTERED、CONFIG.SESSION_EXCHANGE_REQUIRED、CONFIG.NETWORK_PROVIDER_REQUIRED。TikTok 开启支付但未注入后端工厂返回 CONFIG.PAYMENT_PROVIDER_REQUIRED；尚未支持配置支付的平台返回 CONFIG.PAYMENT_UNIMPLEMENTED，均在装配前拒绝。详见 [启动文档](startup.md)。

SDK.DISPOSED 表示底层能力实例已释放，应创建新实例。SDK.DISPOSE_FAILED 是释放异常日志，SDK 会继续清理其他实例；不要据此重新发送业务操作。CALLBACK.EXCEPTION 是上层回调自身抛异常，与平台结果不同。

## 其他能力与不可混淆的结果

- [网络](network.md)：NETWORK.* 表示传输/本地错误。有效 HTTP 4xx/5xx 仍可 Success=true；检查 IsHttpSuccess 及业务协议。
- [支付](payment-pipeline.md)：实际前缀是 PAY.*，不是 PAYMENT.*。PAY.FULFILLMENT_PENDING 表示仍需查询/恢复发货，PAY.NOT_PAID 表示订单未确认付款；不得直接重建付款订单。
- [广告](rewarded-ads.md)：ADS.* 错误与正常提前关闭不同；没有 Error 也不代表 Completed=true，更不证明服务端已发奖。
- [分享](share.md)：SHARE.* 说明调用状态，Dispatched 不证明对方收到分享。
- [存储](storage.md)：STORAGE.NOT_FOUND 与读取失败/格式不匹配分开；未找到数据时再由游戏决定默认值。
- [上报](analytics.md)：ANALYTICS.* 区分参数、平台、取消和超时。TikTok 的无参数 fail 同时代表平台失败或桥接超时，无法提供不存在的详细码；Dispatched 不证明报表入库。

## 日志与排查边界

SDK 生成完整日志，包含时间、接口、参数摘要、CallId、日志 ID、平台、阶段和结果。LogCallback 未提供时由 Unity 输出；提供后 SDK 仍负责格式化。密码、会话、登录 code、签名、请求正文等不直接记录；平台 SDK 自身日志不受公共层日志回调控制。

面板当前显示操作结果和错误，不宣称已经提供“复制完整脱敏诊断报告”功能。先用 CallId 关联 SDK 日志，再在存在 ServerCallId 时查询后端；不要索取或粘贴玩家密钥来代替诊断。接口与官方 SDK 升级应回归已知/未知码、空原始码、重复回调及取消/超时后的迟到结果。

## 主线程调度异常

SDK.Login / SDK.Share / SDK.Pay / SDK.ShowRewardedAd / SDK.Request / 异步 SDK.ReportEvent 的完成派发若遇到 mainThreadDispatch 抛异常，会记录 SDK.DISPATCH_FAILED（Dispatch 阶段、原调用 CallId），不记录异常正文。已收到的平台结果保留，在下一次主线程 SDK.Tick 中交付，不把调度失败伪装成平台失败，也不重试真实交易或平台操作。

UnitySdkHost 自动 Tick；自定义宿主必须持续在主线程 Tick。SDK 不在后台线程直接调用游戏回调。调度器先入队/执行再抛异常时，完成动作最多执行一次；Shutdown/重新初始化后的旧恢复任务被丢弃。调度器应将每个动作可靠地派发到主线程；静默丢弃动作或错误线程执行不属于本次异常恢复保证。

## 结果与错误的调用隔离

聚合层交付的 LoginResult、ShareResult、PaymentResult、StorageResult、RewardedAdResult、NetworkResult、AnalyticsResult 及其中 SdkError 是本次调用的独立快照。底层可以复用结果对象，但 SDK 不会把 CallId/Operation/Stage 写回底层对象；后续请求不会改写游戏已保存结果的关联 ID。异步结果在首次接受回调时先复制，再排队到主线程，排队期间对原错误的 WithRetry 修改不影响本次快照。

这不是对任意并发写入 provider 对象的同步保证；provider 应在调用完成回调时提供一致的数据，不要在回调执行期间并发修改它。游戏仍可对自己持有的错误调用 WithRetry，此操作只影响该结果，不改变其他调用。

## 日志输出契约的回归范围

SDK 生成完整日志行，上层回调只接收内容。时间使用带偏移的 ISO 8601，interface 标识接口，callId/traceId 关联同一次调用，logId 每条不同，params 是 JSON 对象，stage/result 表示阶段及结果。参数按能力输出长度、数量或脱敏占位，不能为调试方便记录会话、分享正文、支付凭据或任意异常原文。

未传上层日志回调时交给引擎输出；上层回调抛异常时，同一条已格式化内容交给默认输出，不重新生成 logId 或时间。Unity 默认输出依级别调用 Debug.Log/LogWarning/LogError。此契约覆盖本 SDK 输出，不拦截官方 SDK、自定义 provider 或游戏自己调用的 Debug.Log。

`tests/LogContractTests.cs` 已并入 run-all-tests.ps1，通过实际聚合调用检查所有能力接口日志、初始化/关闭、Startup 失败、用户回调异常、参数 JSON、字段/ID 关联、敏感值与换行处理，以及上层回调和默认输出的路由与逐字一致性。它验证纯 C# 输出契约，不替代 Unity Console 的视觉验收或第三方 SDK 日志检查。

`tests/UnityLogSinkChecks.cs.txt` 是引擎侧验证：复制到独立测试工程 Assets/Editor，运行 UnityLogSinkChecks.Run（不要传 -quit，检查结束自行退出）。通过 Application.logMessageReceived 观察真实 Play Mode 日志，核对 Info/Warning/Error 映射、回调优先无重复输出、回调异常后的逐字回退。成功标记为 SDK_UNITY_LOG_SINK_CHECKS_PASS。Warning 用关闭登录的同步结果触发；不能取消后立即 Shutdown，再假设队列中的终态已输出。此测试不需要账号或真实平台。
