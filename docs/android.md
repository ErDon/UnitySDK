# Android 原生 SDK 与 Unity 桥接

当前为 **0.1.0 开发预览**，包含可编译的独立 AAR 和 Unity UPM 桥接。原生核心采用 Java 8 兼容接口，Java/Kotlin 应用均可调用，不依赖 Unity 类型或 Kotlin 运行库。要求 Android API 26+；Godot 可复用 AAR，但尚未提供 Godot 桥接。

## 交付与能力

- 原生源码与构建：[android](../android/README.md)。产物：`android/build/erdon-android-0.1.0.aar`。
- 即用 AAR：[UPM 内置文件](../Packages/com.erdon.unitysdk.android/Runtime/Plugins/Android/erdon-android.aar)。版本升级必须与桥接包一起更新。
- Unity 包：`com.erdon.unitysdk.android`，依赖核心、Unity JSON 和 Android JNI 模块。
- [独立工作计划](../android/WORK_PLAN.md)：与原小游戏计划分开计数。

| 能力 | 原生接口 | Unity 上层 | 首版行为 |
| --- | --- | --- | --- |
| 登录 | `NativeSdk.Login` / `login` | `SDK.Login` | 渠道提供已由后端验证的游戏会话；未安装渠道返回不支持 |
| 存储 | `NativeSdk.Storage` / `setStorage/getStorage/removeStorage` | 三个 StorageSync 接口 | SharedPreferences，按 gameId/environment 隔离，不存在与空字符串区分 |
| 分享 | `NativeSdk.Share` / `share` | `SDK.Share` | 系统文本分享；不支持图片时明确拒绝，成功只表示已发起 |
| 激励广告 | `NativeSdk.Ads` / `showAd` | `SDK.ShowRewardedAd` | 渠道扩展，完整观看与提前关闭分开表示 |
| 支付 | `NativeSdk.Payments` / `launchPayment` | `SDK.Pay` | 原生只拉起收银台；Unity 必须显式装配服务端支付工厂 |
| 网络 | `NativeSdk.Network` / `send` | `SDK.Request` | 原生内置可取消的 HTTPS GET/POST；可被渠道覆盖；Unity 复用 HTTP 包及网络工厂 |
| 上报 | `NativeSdk.Analytics` / `report` | `SDK.ReportEvent(event, callback)` | 渠道扩展；同步上报返回 ASYNC_REQUIRED，接受请求不证明后台入库 |

Android 是操作系统，不等于具体登录、支付或广告供应商。本包没有捆绑 Google/Huawei 等渠道，也没有安装后自动可用的支付服务端。仓库现有服务端配置只支持小游戏三平台，不能把 Android 配置改名为微信绕过验证。

## Unity 接入：先跑存储与分享

1. 在 Package Manager 用 Add package from disk 安装核心和 Android 包的 package.json；需要网络时再安装 HTTP 包。
2. 切换 Build Settings 到 Android。在 Android 的 Scripting Define Symbols 添加 `ERDON_PLATFORM_ANDROID`，移除其他 `ERDON_PLATFORM_*`。Minimum API Level 至少 26。
3. 打开 Tools → ErDon SDK → 配置与预览，平台选择 **Android**（不要选择旧的 AndroidChannel 占位项），填写 Game ID 和 App ID（应用包名）。先关闭登录、支付、广告、网络和上报，仅开启存储/分享。
4. 点击“应用构建能力选择”，保存配置。默认生产构建验收阻断仍保留；使用非生产配置和 Development Build。
5. 导入 Android Startup 示例，将组件挂到场景；将 Share 方法绑到按钮。游戏只保留一个启动和关闭负责人。

```csharp
var config = new ErDon.UnitySDK.SdkRuntimeConfiguration {
    platform = "Android", gameId = "my-game", appId = UnityEngine.Application.identifier,
    production = false, login = false, storage = true, share = true
};
ErDon.UnitySDK.UnitySdkHost.BeginConfiguredStartup(config, null, result => {
    if (!result.Success) {
        UnityEngine.Debug.LogError(result.Error.Code + ": " + result.Error.Reason);
        return;
    }
    ErDon.UnitySDK.SDK.SetStorageSync("language", "zh");
});
```

Android 原生启动在 Editor 返回 `PLATFORM.UNAVAILABLE`，Editor 预览请选 Mock。配置预览不代表已安装渠道。原生登录无需小游戏 `ISessionExchange` 工厂，原因是渠道登录实现自身负责后端兑换，不能把平台 token/openId 直接当游戏 Session 返回。

分享规则：Title 为文本标题，Query 非空时作为下一行文本追加；不会按小游戏协议自动拼接分享链接。ImageUrl 非空返回不支持。存储是普通应用私有偏好文件，**不是密钥保险箱**；不要保存 App Secret、支付签名密钥或长期令牌。

## 支付与网络装配

`AndroidPaymentGateway` 实现 `IPlatformPaymentGateway<AndroidPaymentParameters>`。参数包含 `orderId`、`parametersJson`；后者是服务端生成的渠道收银台参数，不能由客户端计算金额或签名。

接入方组合 `UnityPreparedRequestClient<AndroidPaymentParameters>` → `PreparedPaymentLauncher<AndroidPaymentParameters>` → `VerifiedPayment`，并通过 `BeginConfiguredStartup(..., createPayment: ...)` 注入。`matchesOrder` 必须核对订单、商品、金额和币种等渠道字段。原生收银台成功只返回 `AwaitServerConfirmation`；服务端达到 Fulfilled 才代表聚合支付成功。详见 [支付编排](payment-pipeline.md)。

Unity 网络开启时注入 `createNetwork`，复用 [Unity HTTP 接入](network.md)。原生 `AndroidSdk.create` 开启 NETWORK 后默认创建 HttpsNetwork；宿主须声明 INTERNET 权限。支持 HTTPS GET/POST、请求头和 UTF-8 文本，保留非 2xx 状态；不跟随重定向、不禁用证书或主机名校验。默认连接/读取超时各 10 秒、响应上限 1 MiB、请求体上限 64 KiB（按 UTF-8 字节），2 个工作线程、最多排队 32 项。超出队列返回 NETWORK.BUSY。GET 不接受请求体。

聚合取消、超时和关闭会释放支持 CancellableNetwork 的传输句柄；断连异步执行以避免阻塞 UI，仍不撤销服务端动作。旧的 Network 接口实现保持兼容，但不能自动中止它没有暴露句柄的底层请求。自定义网络仍可通过渠道工厂覆盖默认实现。

## 日志、错误及生命周期

Unity 上层继续使用 `SdkConfig.LogCallback`，SDK 提供完整格式，未设置时使用 Unity 引擎输出。Unity 桥接主动接管并抑制底层重复日志；独立原生应用可传 `NativeSdk.LogSink`，未传或回调异常时输出 Logcat。日志包含时间、接口、callId/traceId、logId、阶段与脱敏参数。原生结果的 callId 对应原生操作日志；Unity 上层使用聚合层自己的 CallId。

| 错误 | 原因与处理 |
| --- | --- |
| MODULE.DISABLED | 配置未开启，不能靠渠道安装绕过 |
| CAPABILITY.NOT_SUPPORTED | 未装渠道或默认实现不支持该请求 |
| CONFIG.PAYMENT_PROVIDER_REQUIRED | 未传服务端支付组合工厂 |
| PLATFORM.UNAVAILABLE | 正在 Editor/非 Android 环境运行 |
| INIT.ANDROID_BRIDGE_MISSING | AAR 缺失、未导入或 JNI 类不可用 |
| BRIDGE.VERSION_MISMATCH | AAR 与 Unity 桥接协议不一致，成套升级 |
| BRIDGE.JNI_FAILED / BRIDGE.INVALID_RESPONSE | 原生调用或协议失败；检查打包、保留规则和版本 |
| ANDROID.ACTIVITY_UNAVAILABLE | Activity 已销毁/正在结束；重新创建对应宿主 |
| STORAGE.NOT_FOUND / STORAGE.WRITE_FAILED | 无该键 / 同步持久化失败 |
| NETWORK.BUSY / NETWORK.RESPONSE_TOO_LARGE | 请求队列已满 / 响应超限 |
| NETWORK.TLS_FAILED / NETWORK.PERMISSION_DENIED | TLS 校验失败 / 宿主未授予网络权限 |
| NETWORK.TIMEOUT / NETWORK.INVALID_RESPONSE | 连接或读取超时 / 返回内容不符合 UTF-8 等约束 |
| OPERATION.TIMEOUT / OPERATION.CANCELLED | 本地等待终止，不证明远程动作取消 |

原生默认等待上限 15 秒，AndroidSdk.create 的 timeoutMs 重载或独立 NativeSdk 构造可设 1–300000 毫秒。Unity 桥接广告默认 120 秒、支付 60 秒，其余 15 秒；聚合层另有自己的等待上限。启动/调用/关闭建议均由所属游戏主线程负责；渠道必须迅速返回、异步完成，不能阻塞 UI。原生回调通过配置 executor 派发；Unity JNI 回调进一步派发至 Unity SynchronizationContext，再由聚合层处理。

每个 provider 归 SDK 所有，close/Dispose 幂等；不得销毁其他能力共用的官方全局运行时。关闭会释放在途观察并忽略迟到结果，不删除玩家存储、不退出平台账号、不退款。Activity 重建时关闭旧实例、用新 Activity 创建；Android SDK 不自动恢复支付，应保留原购买幂等键并由服务端恢复。

## 裁剪与升级

UPM 运行时程序集只支持 Editor/Android，并要求 `ERDON_PLATFORM_ANDROID`；AAR importer 禁止 Any Platform/Editor，仅允许 Android 且要求相同宏。构建检查拒绝跨平台宏、错误导入设置和过低 API。七类 C# 实现遵循现有排除宏。

模块开关控制实例化，不能保证删除 AAR 内所有基础实现；AAR 目前包含小型核心及存储/分享实现。第三方渠道应独立 AAR/Gradle 依赖，仅勾选时添加对应依赖；不要把全部供应商打进一个大 AAR 再靠运行时开关假装裁剪。R8 可辅助删除未使用实现，但最终 APK/AAB 资源与 DEX 隔离仍需实测。

本地检查包含原生行为和真实回环 HTTPS、Android API 编译、Unity 桥接检查，以及独立 ARM64 IL2CPP/R8 Development APK 构建。模拟器验证及 APK 检查范围见 [Android 工作计划](../android/WORK_PLAN.md)。**模拟器结果不等于物理真机、系统分享交互或真实渠道登录付款验收，也不覆盖全部构建组合。** 升级时同时重建 AAR，更新版本与校验值，复跑测试，最后执行真机及最终包体审计。

实现依据：[Unity AAR 导入](https://docs.unity3d.com/2022.3/Documentation/Manual/android-aar-import.html)、[AndroidJavaProxy](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/AndroidJavaProxy.html)、[Android 系统分享](https://developer.android.com/training/sharing/send)。

网络实现依据：[Android HttpsURLConnection](https://developer.android.com/reference/javax/net/ssl/HttpsURLConnection)。
