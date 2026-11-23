# 配置面板（开发预览）

Unity 菜单：Tools → ErDon SDK → 配置与预览。

设置存储在接入游戏的 ProjectSettings/ErDonSdk.asset，可随游戏版本管理。只填写 Game ID / App ID，禁止填写服务端密钥。平台只能选择一个，模块可以勾选；界面实时显示缺失参数与未实现能力。

配置现已连接 Editor Mock：进入 Play Mode，选择模拟结果，点击“按配置初始化 Mock”，再点击“调用 SDK.Login”。登录未勾选则返回 MODULE.DISABLED。真实平台配置仍未装配，不能认为已接入。可以模拟成功、取消、拒绝、限流和超时；不生成真实账号或订单。

当前开发版本主动阻止所有 Player 构建：Mock 只支持 Editor，其余平台适配尚未实现。后续接入真实适配后才开放对应目标。阻断发生在 IPreprocessBuildWithReport，不修改其他项目的编译宏、插件导入设置或资源。

此版本不宣称已完成平台裁剪。后续要补：独立平台程序集/依赖包、运行时配置生成、模块注册、插件资源过滤、构建后产物检查。未知平台能力不能静默降级为成功。


## 按配置创建运行时对象

UnitySdkHost.InitializeWithFactory(config, factory) 接收 ISdkProviderFactory。核心按 LoginEnabled / StorageEnabled 决定是否调用 CreateAuth / CreateStorage；未开启模块不调用工厂。工厂必须返回全新对象，不得复用旧实例或在创建过程中重入 SDK。平台官方初始化应在此之前完成。

工厂 CreateAuth 接收主线程派发委托，创建 VerifiedAuth 时传给它，确保授权与会话回调回到 Unity 主线程。可自行组合 IPlatformCredentialProvider 与 ISessionExchange；不强制 HTTP 实现。CreateStorage 返回目标平台 IStorage。

启用模块返回 null 会抛 InvalidOperationException。工厂抛异常时，SDK 回收本次已返回对象，旧运行实例继续有效；工厂内部未返回的局部对象由工厂回收。每次创建必须提供全新的后端 exchange，交付后由 VerifiedAuth 拥有，随后由 SDK 在 Shutdown/重新初始化时释放。

最小工厂示例（接入游戏提供两个创建函数）：

```csharp
using System;
using ErDon.UnitySDK;

sealed class GameSdkFactory : IPlatformSdkProviderFactory
{
    public string Platform { get; private set; }
    readonly Func<Action<Action>, IAuth> createAuth;
    readonly Func<IStorage> createStorage;
    public GameSdkFactory(string platform, Func<Action<Action>, IAuth> auth, Func<IStorage> storage)
    { Platform = platform; createAuth = auth; createStorage = storage; }
    public IAuth CreateAuth(Action<Action> post) { return createAuth(post); }
    public IStorage CreateStorage() { return createStorage(); }
}
```

调用 UnitySdkHost.InitializeWithFactory(config, new GameSdkFactory(config.Platform, authFactory, storageFactory))。关闭的能力对应函数可为空，因为不会被调用。工厂不要预先创建 provider，否则无法达到关闭模块不创建对象的效果。Editor 预览已走此入口。tests/FactoryTests.cs 覆盖开关、失败回收、旧实例保护、重入拒绝及共享对象只释放一次。

此入口负责运行时装配，不等于官方包、JS 或资源已从构建产物裁剪；自动配置生成及真实平台包体核验仍待完成。

## 导出与读取运行时配置

面板的“导出运行时配置 JSON”把 Game ID、App ID、平台、生产标记及全部能力开关导出为 SdkRuntimeConfiguration，不包含 AppSecret、登录凭证或回调。文件由游戏自行纳入资源管理，不自动放入 Resources 或构建产物。导出的数据可以包含未完成的配置，运行前必须 Validate/ToSdkConfig，后者发现错误即抛异常。

```csharp
// configAsset 是游戏加载的 TextAsset；不要把平台密钥放入此文件。
var data = UnityEngine.JsonUtility.FromJson<ErDon.UnitySDK.SdkRuntimeConfiguration>(configAsset.text);
if (data == null) throw new System.InvalidOperationException("Missing SDK configuration");
var config = data.ToSdkConfig();
// 在相应官方 SDK 初始化成功后，用 data.gameId、data.appId、data.Environment
// 创建目标平台工厂，然后调用 UnitySdkHost.InitializeWithFactory(config, factory)。
```

Environment 固定映射 production/test，平台身份必须与所选择的工厂和后端受信应用绑定一致。ToSdkConfig 返回独立配置，后续改动数据对象不影响已返回的开关。schemaVersion 必须为 1；未知版本不会静默降级。原生渠道和上报暂未实现，网络仅放行 Mock/微信；三平台广告已支持，真实平台支付尚无收银台适配。数据校验通过不代表官方依赖、启动或构建已验收。

可使用下述运行时配置入口自动等待平台初始化并装配，面板已接入平台启动按钮；真实平台构建阻断继续保留。Unity 检查覆盖设置转换、JSON 往返与导出对象独立性，界面导出交互尚未视觉验收。

真实平台工厂必须实现 IPlatformSdkProviderFactory，Platform 固定标识其实现的平台；内置工厂已实现。InitializeWithFactory 在创建能力之前核对配置身份，缺失或不一致抛 CONFIG.PLATFORM_MISMATCH，当前 SDK 保持运行。自定义工厂示例中的平台声明必须与其实际 provider 相符，不能只复制配置字符串来掩盖错误实现。此检查不能证明第三方工厂内部调用了正确 SDK，也不替代包体裁剪。

## 配置驱动的平台启动

已安装且编译启用的平台适配包会在 Unity BeforeSceneLoad 自动注册构造器；核心不引用任何平台程序集。请在游戏 Awake/Start 或更晚调用，早于注册阶段会报 CONFIG.PLATFORM_NOT_REGISTERED。

```csharp
var data = UnityEngine.JsonUtility.FromJson<SdkRuntimeConfiguration>(configAsset.text);
// 示例 JSON 应关闭 login；启用时用自己的后端交换器工厂替换 null。
UnitySdkHost.BeginConfiguredStartup(data, null, result => {
    if (!result.Success) {
        UnityEngine.Debug.LogError(result.Error.Code + " traceId=" + result.TraceId);
        return;
    }
    // 此时才能调用已勾选的能力。
});
```

统一入口根据 platform 选择微信/抖音/TikTok 注册项，将 gameId 和 production/test 交给平台工厂，只创建勾选模块。appId 必须配置，但这里不会修改官方插件的 App ID；插件配置和后端应用绑定仍须与它一致。

配置错误在启动前抛出 InvalidOperationException：CONFIG.PLATFORM_NOT_REGISTERED（包未安装、宏未启用或调用过早）、CONFIG.SESSION_EXCHANGE_REQUIRED（启用登录但没有后端工厂）、CONFIG.PAYMENT_UNIMPLEMENTED（真实平台支付未接入）。这类同步错误不调用启动结果回调；平台初始化后的异步失败由 StartupResult 返回。无效配置不会替换已有启动任务或已初始化 SDK。生产 Mock、未实现平台网络和上报继续拒绝。

重复注册同平台抛 CONFIG.DUPLICATE_PLATFORM；每次 Unity Play 会话先清空注册项，再由平台包注册。自定义平台接入可调用 SdkPlatformRegistry.Register，但注册项应只保存构造器，返回新实例；生命周期、错误日志与模块隔离规则不变。现有 JSON 白名单仍只允许首批三平台和 Mock。

已验证公共预检/所有权行为及微信、抖音的 Unity 自动注册。TikTok 注册代码与对应桥接已提供，干净插件工程验证仍待办。自动注册不代表构建裁剪或真机通过，真实平台构建阻断暂不解除。

## 面板启动平台 SDK

进入 Play Mode 后，在“真实平台启动”区域点击“按配置启动平台 SDK”。它读取面板当前配置，调用统一配置入口；成功后显示装配完成及 CallId，失败显示错误码。可取消本地启动等待，或关闭 SDK。未初始化完成时不要调用游戏业务。

开启登录时，游戏需在 Editor Play Mode 的 Awake/Start 中设置 `ErDon.UnitySDK.Editor.EditorPlatformBootstrap.SessionExchangeFactory`，值为每次返回新 ISessionExchange 的构造函数。此代码必须放在 `#if UNITY_EDITOR` 内或 Editor 程序集中，Editor 包不会进入 Player；退出 Play Mode 会清除该工厂引用。只想检查平台启动时可取消勾选登录；面板不创建假登录会话。

真实平台的支付、网络和上报仍未实现时会被预检拒绝。官方 SDK 在 Editor 的能力可能受限，失败或超时不能当作真机结果；面板启动不会自动开启广告或收银台。构建检查仍阻断未验收的真实平台产物。

Mock 区域的业务按钮只在 SDK.ActivePlatform 为 Mock 时可用。改面板下拉框不会改变当前运行平台，必须重新初始化；当前平台标识为只读诊断值，Shutdown 后返回 null。此保护避免切回 Mock 配置后把测试参数发给仍在运行的真实 provider。

## 通用网络接口

新增 INetwork.Send(NetworkRequest, callback)，返回可 Cancel 的 INetworkOperation。UnityNetwork 位于可选 HTTP 包，使用游戏提供的活动 MonoBehaviour 驱动协程。当前已接 SDK 聚合日志和 Mock 配置面板；微信可注入网络工厂，抖音/TikTok 暂不开放；下面是底层接入示例。

```csharp
// 在 MonoBehaviour 内调用；字段保留 provider，并在 OnDestroy 中 Dispose。
network = new UnityNetwork(this); // network 字段类型为 INetwork
var request = new NetworkRequest("https://your-api.example.com/game/config");
var operation = network.Send(request, result => {
    if (!result.Success) {
        UnityEngine.Debug.LogWarning(result.Error.Code);
        return;
    }
    if (!result.IsHttpSuccess) {
        // 请求已收到响应，例如 401/404/500；按业务处理状态码，不自动重试支付等写请求。
        UnityEngine.Debug.LogWarning("HTTP " + result.StatusCode);
        return;
    }
    var text = System.Text.Encoding.UTF8.GetString(result.Body);
    // 按业务解析 text；不要把敏感响应直接输出到日志。
});
// 如需取消：operation.Cancel();
```

请求仅接受 HTTPS，无 URL 内嵌账号密码或 fragment；支持 GET/POST/PUT/PATCH/DELETE/HEAD/OPTIONS，GET/HEAD 不带请求体。请求体最多 1 MiB，超时 1–120 秒，响应上限默认 1 MiB、最大 16 MiB。Headers/Body 在创建和读取时复制，外部修改不改变在途内容；头名须合法，值不可含换行或 NUL。平台限制的请求头仍可能在发送时被拒绝。请求不跟随重定向。

Success 表示拿到了 HTTP 响应；4xx/5xx 仍是 Success=true，IsHttpSuccess 仅对 2xx 为 true。错误包括 NETWORK.UNAVAILABLE（连接失败或超时，当前底层不区分）、NETWORK.RESPONSE_TOO_LARGE、NETWORK.INVALID_RESPONSE、NETWORK.CANCELLED、NETWORK.HOST_UNAVAILABLE、NETWORK.PROVIDER_FAILED 和 SDK.DISPOSED。不返回 Unity 原始网络错误文本。

Cancel 结束底层请求并回调一次取消结果；Dispose 中止所有请求且抑制回调。调用均需主线程，并在宿主销毁前释放 provider。请求停止后不可假定服务端没有执行操作；需要业务幂等键。底层目前没有重试、证书绕过或服务端响应业务校验。流式接收限制已测试，Unity 编译通过；本机 Play Mode 已验证取消、释放、宿主失活拒绝及实际连接超时；正常 HTTPS 响应、平台网络桥接和宿主销毁联调仍待验收。

网络生命周期回归模板为 `tests/NetworkPlayChecks.cs.txt`：复制到隔离工程 Assets/Editor，使用 `-executeMethod NetworkPlayChecks.Run`（不加 `-quit`）运行。通过标记为 SDK_NETWORK_PLAY_PASS。测试创建本机临时 TCP 监听，不完成 TLS，用来验证取消与超时；它不是成功 HTTPS 响应测试。结束时关闭监听和 provider，不修改系统证书或代理设置。

## 聚合网络调用

```csharp
// 在 MonoBehaviour 内初始化；SDK 接管 provider 所有权，不再自行 Dispose 同一实例。
UnitySdkHost.Initialize(new SdkConfig { LoginEnabled = false, NetworkEnabled = true },
    null, networkProvider: new UnityNetwork(this));
var operation = SDK.Request(new NetworkRequest("https://your-api.example.com/config"), result => {
    if (!result.Success) {
        UnityEngine.Debug.LogWarning(result.Error.Code + " callId=" + result.CallId);
        return;
    }
    // HTTP 404/500 仍是已收到响应，按 StatusCode / IsHttpSuccess 处理。
});
// operation.Cancel();
// UnitySdkHost.Shutdown(); // 中止全部在途请求并释放 provider
```

SDK.Request 支持并发，每个返回句柄仅取消自己的请求。首个终态胜出，重复取消、迟到回调不重复通知。NetworkRequest.TimeoutSeconds 同时用于 SDK 本地等待超时；本地超时返回 NETWORK.TIMEOUT 并尝试取消底层。底层先返回连接失败时可能是 NETWORK.UNAVAILABLE。取消只能终止客户端等待，不能撤销服务端操作。

SDK 配置关闭网络时返回 MODULE.DISABLED，INetworkProviderFactory 不创建网络对象；未安装 provider 返回 CAPABILITY.NOT_SUPPORTED。SDK.InitializeWithFactory 支持自定义 INetworkProviderFactory，微信通过可选通信工厂装饰平台工厂；面板和运行时 JSON 对 Mock/微信放行 network 开关。直接注入示例用于验证，不能代替平台网络兼容检查。

结果带 CallId/TraceId，错误包含 Operation=SDK.Request、Stage=Network。日志由 SDK 生成，不输出 URL、查询参数、Headers 或 Body；完成记录 HTTP 状态或统一错误码。用户回调在 Unity 宿主队列派发，因此 Cancel 后不要假定结果已同步送达。Shutdown/重新初始化后旧回调不交给游戏。

## 网络 Mock 预览

面板选 Mock，勾选“网络（Editor Mock）”，进入 Play Mode，选择“网络模拟结果”后重新初始化 Mock，再点击“调用 SDK.Request（不会联网）”。结果展示 HTTP 状态或统一错误码及 CallId。

- HttpOk：本地返回 HTTP 200 与 JSON `{"mock":true}`。
- HttpUnauthorized：本地返回 HTTP 401，Success=true、IsHttpSuccess=false，用于验证业务对 HTTP 状态的处理。
- ConnectionFailed：返回 NETWORK.UNAVAILABLE。
- Timeout：保持请求等待，面板使用 1 秒超时；点击“取消最近一次网络请求”可提前返回 NETWORK.CANCELLED。

取消网络勾选后重新初始化，调用返回 MODULE.DISABLED，不创建网络 provider。多次点击可以创建多个请求，取消按钮仅取消最近的句柄。所有结果本地生成，地址 mock.invalid 不会被请求。关闭或重新初始化 Mock 会清理等待任务。JSON 配置映射 NetworkEnabled；抖音/TikTok 网络配置和未实现的数据上报继续拒绝。

### 微信可选网络工厂

网络配置现对 Mock 和微信开放；抖音/TikTok 网络仍拒绝。微信使用 `BeginConfiguredStartup(..., createNetwork: () => new UnityNetwork(host))` 注入可选 HTTP 实现，面板对应 EditorPlatformBootstrap.NetworkFactory。缺失时返回 CONFIG.NETWORK_PROVIDER_REQUIRED；真实请求兼容性仍待验证。Mock 预览行为不变。
