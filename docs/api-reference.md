# 上层接口索引与生命周期

游戏业务通过 `SDK` 聚合入口调用能力；Unity 接入层通过 `UnitySdkHost` 管理启动与释放。以下索引对应当前源码，包含重载和取消入口，不表示所有平台已完成真机验收。能力是否可用同时取决于配置、编译裁剪、适配器和平台权限。

适配新渠道时，另见[下层扩展接口对照](native-extension.md#扩展接口对照)及 [SdkStartup 生命周期](startup.md)。上层业务不需要直接操作这些 provider。

## 业务接口

| 接口 | 返回/完成方式 | 说明与示例 |
| --- | --- | --- |
| `SDK.Login(callback)` | `LoginResult` 回调 | 默认 15 秒；[登录](authentication.md) |
| `SDK.Login(callback, timeoutMilliseconds)` | 同上 | 显式等待期限，成功返回游戏后端 Session |
| `SDK.CancelLogin()` | 无返回值 | 本地取消产生 AUTH.CANCELLED，不撤销授权或后端会话 |
| `SDK.SetStorageSync(key, value)` | `StorageResult` 同步返回 | [存储](storage.md)，字符串值，不承诺加密 |
| `SDK.GetStorageSync(key)` | `StorageResult` 同步返回 | 不存在返回 STORAGE.NOT_FOUND，与空字符串不同 |
| `SDK.RemoveStorageSync(key)` | `StorageResult` 同步返回 | 删除不存在的键也成功；失败仍需处理 |
| `SDK.Share(request, callback, timeoutMilliseconds)` | `ShareResult` 回调 | [分享](share.md)，默认 15 秒；成功不证明送达 |
| `SDK.CancelShare()` | 无返回值 | 结束本地等待，不保证关闭分享面板 |
| `SDK.ShowRewardedAd(request, callback, timeoutMilliseconds)` | `RewardedAdResult` 回调 | [广告](rewarded-ads.md)，默认 120 秒；提前关闭可成功但 Completed=false |
| `SDK.CancelRewardedAd()` | 无返回值 | 不保证关闭广告；下层未结束时仍可能拒绝新展示 |
| `SDK.Pay(request, callback, timeoutMilliseconds)` | `PaymentResult` 回调 | [支付](payment-pipeline.md)，默认 60 秒；真实收银台尚待接入 |
| `SDK.CancelPay()` | 无返回值 | 不退款、不证明未扣费；保留购买编号查单 |
| `SDK.Request(request, callback)` | `INetworkOperation` | [网络](network.md)，每个句柄独立 Cancel；HTTP 非 2xx 不一定是传输失败 |
| `SDK.ReportEvent(event, callback, timeoutMilliseconds)` | `IAnalyticsOperation` | [上报](analytics.md)，默认 15 秒；推荐跨平台统一使用此重载 |
| `SDK.ReportEvent(event)` | `AnalyticsResult` 同步返回 | 仅用于同步适配；TikTok 异步实现返回 ANALYTICS.ASYNC_REQUIRED |
| `SDK.ActivePlatform` | `string` 属性 | 当前已装配平台；未初始化/关闭后为 null，不代表新启动已完成 |

结果对象各自携带调用编号；失败读取 `Error.Code`、`Reason` 和关联字段。不要将不同能力的 Success/Dispatched/Completed 解释成同一个业务状态。详见[错误诊断](errors.md)。所有游戏入口在主线程执行。

## 启动、装配与关闭

| 接口 | 接入责任 |
| --- | --- |
| `UnitySdkHost.BeginConfiguredStartup(...)` | 推荐入口：运行时配置、会话/网络工厂、就绪回调及可选日志回调；按目标平台注册装配 |
| `UnitySdkHost.BeginStartup(...)` | 自行提供配置、官方 runtime 桥接和平台工厂；见[启动](startup.md) |
| `UnitySdkHost.CancelStartup()` | 取消当前初始化等待，已运行的业务 SDK 保持运行 |
| `UnitySdkHost.InitializeWithFactory(...)` | 官方宿主已经就绪时，由工厂按开关创建能力；成功后放弃旧启动等待 |
| `UnitySdkHost.Initialize(...)` | 官方宿主已就绪，手动提供各能力的新实例；所有权交给 SDK |
| `UnitySdkHost.Shutdown()` | Unity 游戏结束 SDK 使用时调用；关闭启动尝试和业务能力，旧业务回调不再交付 |
| `SDK.InitializeWithFactory(...)` / `SDK.Initialize(...)` | 引擎接入层入口，须提供主线程派发和引擎日志回退；Unity 游戏优先用宿主版本 |
| `SDK.Tick()` | 自定义引擎宿主定期驱动超时；UnitySdkHost 已自动调用，不额外重复驱动 |
| `SDK.Shutdown()` | 只释放业务 SDK，不处理 UnitySdkHost 持有的启动尝试 |

全局 SDK 只有一个当前实例。游戏应指定唯一生命周期负责人；临时界面退出只取消其等待，不能随意 Shutdown 其他界面共享的 SDK。关闭会抑制在途业务回调，负责人需要自行收起等待 UI。对同一 SDK 重复 Shutdown 不重复释放 provider。

## 日志回调：SDK 负责生成内容

下面仅演示 Editor 中无能力初始化和日志接收；实际平台使用自己的配置及启动流程。回调收到完整格式化内容，游戏只转发到日志系统，不重新拼接 SDK 的时间、接口、参数或编号。未传回调、或者回调抛异常时由 Unity 默认日志接管。

```csharp
using ErDon.UnitySDK;

var config = new SdkConfig { Platform = "Mock", LoginEnabled = false };
config.LogCallback = (level, message) => {
    // 此处可转发到游戏日志系统；不能在日志回调内重入 SDK。
    if (level == SdkLogLevel.Error) UnityEngine.Debug.LogError(message);
    else if (level == SdkLogLevel.Warning) UnityEngine.Debug.LogWarning(message);
    else UnityEngine.Debug.Log(message);
};
UnitySdkHost.Initialize(config, null);
// 唯一负责人不再使用 SDK 时执行（不是每次业务调用后执行）：
UnitySdkHost.Shutdown();
```

配置启动时将同样的回调传给 `BeginConfiguredStartup` 的 `logCallback` 参数，省略即使用 Unity 回退。日志参数由 SDK 脱敏；登录会话、支付回执、网络正文和存储值不会原样输出。第三方官方插件自己的日志不受此回调控制。

## 本地取消示例

下面四个调用应分别连接对应能力的“停止等待”操作，不能作为每帧清理逻辑。没有当前等待时不会发起平台操作。

```csharp
using ErDon.UnitySDK;

SDK.CancelLogin();
SDK.CancelShare();
SDK.CancelRewardedAd();
SDK.CancelPay();
```

网络和异步上报的取消通过各自返回句柄 `operation.Cancel()` 执行，示例见对应能力文档；它们允许多个并发调用，没有“取消所有业务”的同名全局接口。退出游戏使用 `UnitySdkHost.Shutdown()` 统一释放。
