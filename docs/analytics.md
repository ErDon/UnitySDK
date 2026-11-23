# 数据上报（公共聚合与三平台适配已实现）

当前已提供 `SDK.ReportEvent`、`AnalyticsEvent`、`AnalyticsResult`、`IAnalytics` 和可选工厂接口。手动初始化可通过 `SdkConfig.AnalyticsEnabled` 启用，并传入 `analyticsProvider` 或实现 `IAnalyticsProviderFactory`。三平台工厂已提供适配，相应运行时 JSON 可设置 analytics=true。TikTok 必须使用回调重载，等待平台结果；Mock 配置已接通 Editor 预览。真实构建仍须完成整体产物验收。

## TikTok 异步能力

本地 1.1.6-Release 的 Runtime 与 Editor DLL 都提供 `TT.ReportEvent(ReportEventParam)`，字段为 eventName、JsonData params 及无参数 success/fail/complete 回调。成功回调只代表平台报告成功，不能证明后台报表入库。两套 DLL 的桥接失败与超时都转为同一个 fail 回调，丢弃 ErrorInfo；适配器返回 ANALYTICS.PLATFORM_FAILED，原因明确为平台拒绝或桥接超时，不编造原始错误码。

`TikTokAnalytics` 实现 `IAsyncAnalytics.ReportEvent(event, callback)`，返回 `IAnalyticsOperation`。首个 success/fail/complete 信号决定结果；单独 complete 返回 ANALYTICS.INVALID_RESULT。Cancel 只取消本地观察并返回 ANALYTICS.CANCELLED，不撤销已发送事件；Dispose 抑制未完成操作的回调。底层没有平台返回时保持等待；SDK 聚合层默认 15 秒超时，并取消底层的本地观察。调用、取消、Tick 和释放需在 Unity 主线程执行。统一入口使用下述回调重载。

参数逐项构造官方 JsonData，空参数用 JsonType.Object，避免被编码为 null 或字符串。测试同时引用本地 TTSDK DLL 和其 LitJson/TTLitJson.dll，覆盖成功/失败/重复/迟到回调、complete 单独触发、同步抛异常、本地取消、释放、空参数及字段映射。运行 `tests/run-tiktok-tests.ps1`。原本地插件含定制修改；另已针对干净官方 1.1.6 原件完成 Runtime/Editor 双 DLL 回归及独立工程集成，详见 [兼容矩阵](../compatibility/README.md)。这些编译与注入检查不代表真机上报或后台入库验收。

## 参数与结果

事件使用独立快照，上层修改原始字典或读取到的字典不影响事件。允许 String、Int32、有限 Double 值；不接受 null、集合、自定义对象、布尔值或其他数字类型。需要其他类型时由业务显式转换并与平台后台事件定义保持一致。

SDK 自身限制：事件名和参数名为 1–64 个 ASCII 字母、数字或下划线；每个事件最多 32 个参数，字符串值最多 1024 个 UTF-16 字符。这些是公共契约限制，不代表平台配额或后台配置要求。平台适配仍需验证目标平台的额外要求。

```csharp
// 先初始化 SDK，并启用数据上报；后台将 mode 字段定义为文本。
var analyticsEvent = new ErDon.UnitySDK.AnalyticsEvent("level_end",
    new System.Collections.Generic.Dictionary<string, object> {
        { "mode", "solo" }
    });
ErDon.UnitySDK.IAnalyticsOperation operation = ErDon.UnitySDK.SDK.ReportEvent(
    analyticsEvent, result => {
        if (result.Dispatched)
            UnityEngine.Debug.Log("上报调用完成，CallId=" + result.CallId);
        else
            UnityEngine.Debug.LogWarning(result.Error.Code + ": " + result.Error.Reason
                + "，CallId=" + result.CallId);
    }, timeoutMilliseconds: 15000);
// 不再等待时调用 operation.Cancel()；这不会撤回已发送事件。
```

此示例使用字符串参数，兼容已接入的微信字符串接口基线；数字参数仅在目标适配支持时使用，详见下方“微信字符串接口基线”。

本地微信官方包 0.1.23 的 `Runtime/WX.cs` 中，`WX.ReportEvent<T>(string eventId, T data)` 返回 void，未提供上报接收确认回调；注释标明最低基础库 2.14.4。对应文档入口为 [wx.reportEvent](https://developers.weixin.qq.com/minigame/dev/api/data-analysis/wx.reportEvent.html)（本轮在线读取未成功，以本地签名为依据）。

因此 `AnalyticsResult.Dispatched` 只表示平台调用正常返回，不能证明数据送达、后台接受或报表入库。不为其提供有歧义的 Success 字段。错误结果包含统一 `SdkError`；聚合入口为每次调用生成 CallId/TraceId，并关联错误 Operation/Stage 和日志。不得据此结果结算奖励或确认支付，不自动重试以免产生重复统计。

## 调用与生命周期

不带回调的重载是同步接口，只适用于微信和抖音；TikTok 返回 ANALYTICS.ASYNC_REQUIRED 且不发送事件。跨平台业务应统一使用回调重载。调用需与其他 SDK 生命周期调用一起在 Unity 主线程执行。平台实现不得重入 SDK。模块关闭时工厂不会创建上报实例；初始化失败只释放本次新建的实例，保留之前正常工作的 SDK。SDK 取得实例所有权，关闭时释放一次，即使实例同时实现存储等其他能力也不重复释放。

日志由 SDK 统一输出，包含时间、接口名、调用 ID、阶段和结果。事件名称、参数名称及参数值不直接写入日志，仅输出参数数量及脱敏标记。同步接口返回只能观察本地调用结果。回调重载观察平台可提供的完成信号，但两种形式都不是后台入库证明。

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

`tests/AnalyticsContractTests.cs` 覆盖事件字典隔离、空参数、无效名称、数量和长度边界、非有限数字和不支持的类型、派发结果语义。`tests/AnalyticsFacadeTests.cs` 覆盖模块开关、配置快照、按需创建、所有权/回收、重入保护、结果隔离、日志脱敏与调用关联。通过公共回归入口 `tests/run-all-tests.ps1` 执行。另有下述真实官方程序集编译及注入行为检查；尚无真机上报或真实数据入库验证。

## 微信接入

先按 [微信接入文档](wechat.md) 安装所选官方插件基线并完成微信运行时初始化。下述泛型路径针对本地 0.1.23；固定官方提交 915d4bf 使用本页末尾的字符串接口路径。配置 platform=WeChat、analytics=true，通过已有注册启动入口创建 WeChatProviderFactory；若手动初始化，使用相同工厂并设置 AnalyticsEnabled=true。事件名和字段需与微信后台事件配置对应，不自动添加游戏/环境字段；不同游戏通过各自微信 App ID 和工程配置隔离，业务确需环境维度时自行配置并传入。

WeChatAnalytics 使用 `WX.ReportEvent<Dictionary<string,object>>`。本地 WX.dll 到 wx-runtime-editor.dll 的调用链检查表明该方法调用 LitJson.JsonMapper.ToJson，然后传递 JSON 字符串。没有平台成功或失败回调，无法判断静默丢弃、后台配置缺失或网络未送达。可观察到的同步异常转为 ANALYTICS.PROVIDER_FAILED，不返回异常原文；释放后的调用返回 SDK.DISPOSED。

`tests/run-wechat-factory-tests.ps1` 新增 WeChatAnalyticsTests：引用实际 Wx.dll、wx-runtime-editor.dll 和官方 LitJson.dll，验证编译、序列化标量、参数映射、快照、异常、释放和按需工厂。未调用真实微信容器；目标小游戏内运行、字段入库与升级回归仍待真机验证。

## 抖音接入

官方 TTSDK 6.9.7 的 `TT.ReportAnalytics<T>(string, Dictionary<string,T>)` 返回 void，无接收确认回调。官方附带 ttsdk.xml 要求先在后台创建事件、配置事件名和字段。统一接口通过 Dictionary<string,object> 传递参数，继续使用公共的 String/Int32/Double 子集，不自行改变事件名或追加字段。

本地官方 DLL 中 `TTAPIImpl.ReportAnalytics<T>` 先设置内部字段 `cp_event_key_name`，再逐项构造 `TTSDK.UNBridgeLib.LitJson.JsonData(value)`。因此适配器拒绝业务参数使用该名称，返回 `ANALYTICS.RESERVED_PARAMETER`，避免覆盖事件名。官方实现自身可能输出事件名；SDK 自己的日志脱敏不代表第三方插件日志也已脱敏，应按官方插件配置管理其日志。

初始化 platform=Douyin、analytics=true，由 DouyinProviderFactory 按需创建。同步异常返回 ANALYTICS.PROVIDER_FAILED，释放后返回 SDK.DISPOSED。不自动重试、不确认后台入库。`tests/run-douyin-tests.ps1` 包含实际 DLL 编译、官方 JsonData 标量构造/序列化、参数映射、保留字段拒绝和释放检查；真机送达尚未验证。

## 跨平台统一回调入口

```csharp
using ErDon.UnitySDK;

var analyticsEvent = new AnalyticsEvent("level_end",
    new System.Collections.Generic.Dictionary<string, object> { { "mode", "solo" } });
IAnalyticsOperation operation = SDK.ReportEvent(analyticsEvent, result => {
    if (result.Dispatched)
        UnityEngine.Debug.Log("上报调用完成，CallId=" + result.CallId);
    else
        UnityEngine.Debug.Log(result.Error.Code + ": " + result.Error.Reason);
}, timeoutMilliseconds: 15000);
// 如业务不再需要等待：operation.Cancel();
```

微信、抖音在同步接口正常返回后派发结果；TikTok 等待其 success/fail 回调。支持多次并发，每次拥有独立 ID、取消和超时；超时范围 1–120000 毫秒，默认15000。由 SDK.Tick（UnitySdkHost 自动驱动）检查本地超时，超时返回 ANALYTICS.TIMEOUT，取消返回 ANALYTICS.CANCELLED。取消不撤销平台事件，也不触发自动重试。

有效调用的回调经初始化时的主线程派发器投递；派发器抛异常时由后续 Tick 恢复。初始化/模块/参数预检失败会在当前调用线程直接返回回调，因此调用本身也必须位于主线程。重复或迟到平台结果被抑制，Shutdown 丢弃排队结果并取消未完成观察。回调异常进入 SDK 格式化日志。平台实现返回 null 结果/句柄分别映射 ANALYTICS.INVALID_RESULT/ANALYTICS.INVALID_OPERATION。

`tests/AnalyticsAsyncTests.cs` 验证并发独立结果、取消、超时、快照、同步平台兼容、派发恢复和关闭后的回调抑制。TikTok 双 DLL 测试另验证工厂和运行时配置。Unity Mock Play Mode、TikTok 干净官方插件双 DLL 回归与独立工程集成已验证；真机上报和后台入库仍待验证。

## Editor Mock 面板

在 SDK 配置面板勾选数据上报、选择 Mock 平台并进入 Play Mode。选择“上报模拟结果”后重新初始化 Mock，再点击 ReportEvent（不发送数据）。Dispatched 返回模拟调用完成，Failed 返回 ANALYTICS.PLATFORM_FAILED，Timeout 保持等待直到面板所设的预览等待上限；也可点击“取消最近一次上报”。未勾选时调用返回 MODULE.DISABLED。

Mock 位于 Editor 程序集，不发送统计请求。改变模拟结果后需要重新初始化才能生效；改变配置开关也不修改已经初始化的 SDK 快照。真实平台运行时，这组 Mock 业务按钮保持禁用。面板按钮逻辑和 Play Mode 验证不同于视觉交互验收，后者仍需单独检查。

隔离工程可复制 `tests/AnalyticsPlayChecks.cs.txt` 到 Assets/Editor/AnalyticsPlayChecks.cs，通过 AnalyticsPlayChecks.Run 启动行为测试。测试包含真实 UnitySdkHost 的 Update 派发和超时驱动、模块关闭及关闭后排队结果抑制。

## 微信字符串接口基线

官方仓库 `wechat-miniprogram/minigame-tuanjie-transform-sdk` 提交 `915d4bf4f60ef0cd1c29316a8d67257185d609a7`（包标识0.1.1）与本地0.1.23的接口不同：前者 `WX.ReportEvent` 接受 `Dictionary<string,string>`。使用该固定提交时，在目标平台编译宏中同时设置 `ERDON_PLATFORM_WECHAT;ERDON_WECHAT_STRING_ANALYTICS`。不要根据版本数字大小自动判断接口形式。

兼容宏开启后，字符串和空参数事件可正常派发；Int32/Double 参数返回 `ANALYTICS.PARAMETER_TYPE_UNSUPPORTED`，不会调用官方接口，不会隐式转换成字符串。若业务主动将字段设计成文本，可显式传字符串，但须同时按文本定义后台事件字段。本地0.1.23继续使用原泛型路径，不开启该宏，保留数值类型。此开关只覆盖已核对的上报签名，不代表该官方包全部能力及真机已验收。

`tests/run-wechat-string-analytics-tests.ps1` 接收官方包路径与该包在 Unity 中编译的 Wx.dll，验证真实签名编译、数字拒绝、字符串/空参数派发、快照和释放。旧基线继续运行 `run-wechat-factory-tests.ps1`。两者不得混用 Wx.dll 与插件包。
