# Android/iOS 渠道扩展边界

现有通用扩展契约继续保留；新增的 Android 原生 AAR 与 Unity 桥接见 [Android 接入](android.md)。目前不集成具体 Android/iOS 供应商。Android 与 iOS 是运行系统，渠道登录、广告、支付供应商应分别确定。本文中的 ExampleChannel 是说明用标识，不是已实现的平台。

## 下层适配，上层保持聚合

渠道包依赖 `com.erdon.unitysdk`，实现 `IPlatformSdkProviderFactory`。其中 Platform 必须与 SdkConfig、IPlatformRuntime 的标识完全一致，例如 Android.ExampleChannel。运行时桥接实现 IPlatformRuntime：Initialize 完成后设置 IsReady，再回调 null 表示无错误；Dispose 解除本次初始化观察，不销毁仍被能力实例使用的官方运行时。

只实现需要的能力接口：IAuth、IStorage、IShare、IRewardedAds、IPay、INetwork、IAnalytics。附加能力通过对应的可选工厂接口按需创建；异步上报实现同时提供 IAnalytics/IAsyncAnalytics。若平台只能异步上报，同步接口返回 ANALYTICS.ASYNC_REQUIRED，不提前宣称成功。

工厂构造函数只保存配置，不初始化供应商或创建实例。每次创建返回新的 SDK 自有实例；模块未勾选时不调用对应工厂。SdkStartup 等待运行时就绪后才装配。创建失败只回收本次新建实例，保留旧 SDK；实例的 Dispose 需要幂等，不能清除玩家持久化数据。上层继续调用 SDK.Login、SDK.GetStorageSync、SDK.Pay 等聚合入口，无需知道供应商类型。

登录建议复用 IPlatformCredentialProvider + VerifiedAuth + ISessionExchange：渠道临时凭证经服务端验证后才生成游戏会话。广告 Completed 是客户端结果；支付必须经订单服务验证并达到 Fulfilled，不能凭收银台成功回调发货。接口实现不依赖具体游戏的角色、UI、热更新或异步框架。

## 扩展接口对照

这些接口供渠道适配层实现，游戏业务仍调用 SDK 聚合入口。下表中的 Dispose 负责释放包装器、监听和在途观察，不代表退出平台账号、退款或删除存储。

| 契约 | 方法/属性与责任 | 详细说明 |
| --- | --- | --- |
| IPlatformRuntime | Platform、IsReady、Initialize(callback)、Dispose；初始化成功必须已经就绪 | [启动与所有权](startup.md) |
| IPlatformSdkProviderFactory / ISdkProviderFactory | Platform、CreateAuth(dispatch)、CreateStorage；只创建新实例 | [工厂示例](configuration.md) |
| IShareProviderFactory / IRewardedAdsProviderFactory | CreateShare / CreateRewardedAds；仅在相应模块开启时创建 | [分享](share.md)、[广告](rewarded-ads.md) |
| IPayProviderFactory | CreatePay(dispatch)；将订单服务和收银台桥接组合为 IPay | [支付编排](payment-pipeline.md) |
| INetworkProviderFactory / IAnalyticsProviderFactory | CreateNetwork / CreateAnalytics；未安装相应通信或渠道实现时不返回假成功 | [网络](network.md)、[上报](analytics.md) |
| IPlatformCredentialProvider / ISessionExchange | GetCredential / Exchange；分别取得一次性凭证、兑换游戏会话 | [登录](authentication.md) |
| IAuth | Login(callback)、Dispose；成功结果应为已验证的游戏会话 | [登录](authentication.md) |
| IStorage | Set、Get、Remove、Dispose；字符串存储，区分不存在与空值 | [存储](storage.md) |
| IShare | Share(request, callback)、Dispose；只承诺平台接受发起请求 | [分享](share.md) |
| IRewardedAds | Show(request, callback)、Dispose；完整观看与提前关闭必须区分 | [广告](rewarded-ads.md) |
| IPay / IPaymentLauncher | Pay / Launch；聚合支付结果与渠道收银台结果不能混用 | [支付编排](payment-pipeline.md) |
| IOrderClient | Create、Get、Confirm、Dispose；保留购买幂等编号，发货状态由服务端决定 | [订单](orders.md) |
| INetwork / INetworkOperation | Send 返回独立取消句柄，provider 可 Dispose；Cancel 不撤销服务器动作 | [网络](network.md) |
| IAnalytics / IAsyncAnalytics / IAnalyticsOperation | 同步或异步 ReportEvent；异步返回独立 Cancel 句柄，完成不证明后台入库 | [上报](analytics.md) |

能力工厂没有统一 Dispose 方法：工厂只持有创建配置，成功返回的能力对象由 SDK 管理；尚未返回给 SDK 的中间对象发生异常时由工厂自行回收。同一官方全局运行时可能服务多个能力，各包装器不能在 Dispose 时擅自销毁其他能力仍在使用的全局对象。不要通过返回同一个旧 provider 来复用上一轮 SDK 生命周期。

## 当前能用的启动路径

当前 `SdkRuntimeConfiguration` 及面板支持 Mock、微信、抖音、TikTok 和 Android。Android 是新增原生包的固定身份，不代表任意渠道名字都可通过校验。即使调用 SdkPlatformRegistry.Register 注册自定义名字，也不会绕过配置校验；AndroidChannel/IOSChannel 面板选项不是可发布适配。不要把原生渠道伪装成微信以绕过校验。

扩展开发可使用已存在的手动启动入口，不修改公共配置白名单：

```csharp
using ErDon.UnitySDK;

// vendorRuntime/vendorFactory 由渠道包实现，不是此仓库自带对象。
var config = new SdkConfig {
    Platform = "Android.ExampleChannel",
    LoginEnabled = false,
    StorageEnabled = true
};
UnitySdkHost.BeginStartup(config, vendorRuntime, vendorFactory, result => {
    if (!result.Success)
        UnityEngine.Debug.Log(result.Error.Code + ": " + result.Error.Reason);
});
```

初始化、取消与关闭在 Unity 主线程调用，由 UnitySdkHost 驱动 Tick 和回调队列。原生线程回调交由聚合层派发，适配器不直接调用游戏 UI。每个接口需要处理重复/迟到结果和释放；取消本地等待不表示取消供应商交易。SDK 统一生成日志，上层可通过 SdkConfig.LogCallback 接收；供应商自身日志需独立配置。

## 供应商接入时必须补齐

1. 单独 UPM 包、固定官方依赖版本、平台与能力清单、官方包来源和升级快照。
2. 原生工程步骤：Android manifest/Gradle/AAR/SO，或 iOS plist/framework/链接参数；由具体供应商要求确定，不能先写通用空配置假装支持。
3. 扩展运行时配置、面板平台选择、注册与构建审计规则，再开放该渠道。保留不支持能力的明确错误。
4. 平台程序集采用 Android/iOS 与专用宏约束；原生插件的 PluginImporter 平台设置需单独配置。asmdef 限制不能替代 AAR、SO、framework、资源文件的裁剪检查。
5. 对目标构建输出检查非目标 SDK/原生库/资源是否缺席，再做真机登录、广告、支付恢复等验收。当前真实 Player 构建仍阻断，本文不授权绕过阻断。

## 已验证与未验证

`tests/NativeExtensionTests.cs` 使用显式测试替身验证两种自定义平台身份、延迟装配、按能力多态、关闭模块不创建、所有权与身份不匹配拒绝；同时确认便携配置仍拒绝未实现原生渠道。可通过 tests/run-all-tests.ps1 重跑。

该测试证明现有接口允许扩展，不证明任何原生 SDK、JNI/Objective-C 桥接、原生构建或真机功能可用。首批供应商选择、接入与验收需另行迭代；此处是原有通用扩展测试的边界；新增 Android 包的具体实现与验证单独列在 [Android 工作计划](../android/WORK_PLAN.md)。
