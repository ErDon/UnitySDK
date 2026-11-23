# 数据上报（公共聚合与微信、抖音适配已实现）

当前已提供 `SDK.ReportEvent`、`AnalyticsEvent`、`AnalyticsResult`、`IAnalytics` 和可选工厂接口。手动初始化可通过 `SdkConfig.AnalyticsEnabled` 启用，并传入 `analyticsProvider` 或实现 `IAnalyticsProviderFactory`。微信和抖音工厂已提供适配，相应运行时 JSON 可设置 analytics=true；面板标记支持微信、抖音。TikTok 和 Mock 上报尚未接通，对应配置仍拒绝启用。真实构建仍须完成整体产物验收。

## 参数与结果

事件使用独立快照，上层修改原始字典或读取到的字典不影响事件。允许 String、Int32、有限 Double 值；不接受 null、集合、自定义对象、布尔值或其他数字类型。需要其他类型时由业务显式转换并与平台后台事件定义保持一致。

SDK 自身限制：事件名和参数名为 1–64 个 ASCII 字母、数字或下划线；每个事件最多 32 个参数，字符串值最多 1024 个 UTF-16 字符。这些是公共契约限制，不代表平台配额或后台配置要求。平台适配仍需验证目标平台的额外要求。

```csharp
var analyticsEvent = new AnalyticsEvent("level_end",
    new Dictionary<string, object> {
        { "level", 4 },
        { "duration", 12.5 },
        { "mode", "solo" }
    });
// 先初始化 SDK，并提供启用的数据上报实现。
var result = SDK.ReportEvent(analyticsEvent);
if (!result.Dispatched)
    UnityEngine.Debug.Log(result.Error.Code + ": " + result.Error.Reason);
```

本地微信官方包 0.1.23 的 `Runtime/WX.cs` 中，`WX.ReportEvent<T>(string eventId, T data)` 返回 void，未提供上报接收确认回调；注释标明最低基础库 2.14.4。对应文档入口为 [wx.reportEvent](https://developers.weixin.qq.com/minigame/dev/api/data-analysis/wx.reportEvent.html)（本轮在线读取未成功，以本地签名为依据）。

因此 `AnalyticsResult.Dispatched` 只表示平台调用正常返回，不能证明数据送达、后台接受或报表入库。不为其提供有歧义的 Success 字段。错误结果包含统一 `SdkError`；聚合入口为每次调用生成 CallId/TraceId，并关联错误 Operation/Stage 和日志。不得据此结果结算奖励或确认支付，不自动重试以免产生重复统计。

## 调用与生命周期

这是同步接口，需与其他 SDK 生命周期调用一起在 Unity 主线程执行。平台实现不得重入 SDK。模块关闭时工厂不会创建上报实例；初始化失败只释放本次新建的实例，保留之前正常工作的 SDK。SDK 取得实例所有权，关闭时释放一次，即使实例同时实现存储等其他能力也不重复释放。

日志由 SDK 统一输出，包含时间、接口名、调用 ID、阶段和结果。事件名称、参数名称及参数值不直接写入日志，仅输出参数数量及脱敏标记。没有单独的网络送达回调或超时：同步接口返回只能观察本地调用结果。

| 错误码 | 原因与处理 |
| --- | --- |
| SDK.NOT_INITIALIZED | 先初始化 SDK |
| MODULE.DISABLED | 初始化配置未启用 AnalyticsEnabled |
| CAPABILITY.NOT_SUPPORTED | 直接初始化未传入上报实现 |
| ANALYTICS.INVALID_ARGUMENT | 传入了 null 事件；事件构造本身的参数错误会抛 ArgumentException |
| ANALYTICS.INVALID_RESULT | 平台实现返回 null，需修复适配器 |
| ANALYTICS.PROVIDER_FAILED | 平台实现抛异常或重入 SDK；不透传异常中的敏感内容 |

工厂初始化启用上报却未提供对应工厂或实例时，会拒绝初始化，而不是静默降级。平台实现可通过 SdkError 返回平台错误码和原因，结果与错误在聚合层复制，避免后续调用改写已返回结果。

## 已验证范围

`tests/AnalyticsContractTests.cs` 覆盖事件字典隔离、空参数、无效名称、数量和长度边界、非有限数字和不支持的类型、派发结果语义。`tests/AnalyticsFacadeTests.cs` 覆盖模块开关、配置快照、按需创建、所有权/回收、重入保护、结果隔离、日志脱敏与调用关联。通过公共回归入口 `tests/run-all-tests.ps1` 执行。尚无平台调用或真实数据入库验证。

## 微信接入

使用官方 0.1.23 Unity 插件并先完成微信运行时初始化。配置 platform=WeChat、analytics=true，通过已有注册启动入口创建 WeChatProviderFactory；若手动初始化，使用相同工厂并设置 AnalyticsEnabled=true。事件名和字段需与微信后台事件配置对应，不自动添加游戏/环境字段；不同游戏通过各自微信 App ID 和工程配置隔离，业务确需环境维度时自行配置并传入。

WeChatAnalytics 使用 `WX.ReportEvent<Dictionary<string,object>>`。本地 WX.dll 到 wx-runtime-editor.dll 的调用链检查表明该方法调用 LitJson.JsonMapper.ToJson，然后传递 JSON 字符串。没有平台成功或失败回调，无法判断静默丢弃、后台配置缺失或网络未送达。可观察到的同步异常转为 ANALYTICS.PROVIDER_FAILED，不返回异常原文；释放后的调用返回 SDK.DISPOSED。

`tests/run-wechat-factory-tests.ps1` 新增 WeChatAnalyticsTests：引用实际 Wx.dll、wx-runtime-editor.dll 和官方 LitJson.dll，验证编译、序列化标量、参数映射、快照、异常、释放和按需工厂。未调用真实微信容器；目标小游戏内运行、字段入库与升级回归仍待真机验证。

## 抖音接入

官方 TTSDK 6.9.7 的 `TT.ReportAnalytics<T>(string, Dictionary<string,T>)` 返回 void，无接收确认回调。官方附带 ttsdk.xml 要求先在后台创建事件、配置事件名和字段。统一接口通过 Dictionary<string,object> 传递参数，继续使用公共的 String/Int32/Double 子集，不自行改变事件名或追加字段。

本地官方 DLL 中 `TTAPIImpl.ReportAnalytics<T>` 先设置内部字段 `cp_event_key_name`，再逐项构造 `TTSDK.UNBridgeLib.LitJson.JsonData(value)`。因此适配器拒绝业务参数使用该名称，返回 `ANALYTICS.RESERVED_PARAMETER`，避免覆盖事件名。官方实现自身可能输出事件名；SDK 自己的日志脱敏不代表第三方插件日志也已脱敏，应按官方插件配置管理其日志。

初始化 platform=Douyin、analytics=true，由 DouyinProviderFactory 按需创建。同步异常返回 ANALYTICS.PROVIDER_FAILED，释放后返回 SDK.DISPOSED。不自动重试、不确认后台入库。`tests/run-douyin-tests.ps1` 包含实际 DLL 编译、官方 JsonData 标量构造/序列化、参数映射、保留字段拒绝和释放检查；真机送达尚未验证。
