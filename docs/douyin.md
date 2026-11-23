# 抖音适配（开发中）

独立 UPM 包 com.erdon.unitysdk.douyin 已实现 DouyinCredentialProvider。上层 SDK.Login 不变，下层强制请求已登录账号；isLogin=false 返回 AUTH.NOT_LOGGED_IN，空 code 返回 AUTH.INVALID_CREDENTIAL，匿名 code 不作为游戏账号凭据。成功取得 code 后仍必须经 VerifiedAuth 和抖音服务端验证器交换会话。后端已有 createDouyinVerifier，尚未真实平台联调。

## 官方接入来源

- [账号接口](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/api/c-api/open-ability/account)：抖音登录回调包含 code、anonymousCode、isLogin，与 TikTok 的单 code 回调不同。
- [安装 TTSDK](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/game-engine/rd-to-SCgame/unity-game-access/install-connect/install-sdk)：通过 BGDT 登录并安装 TTSDK。
- [BGDT 官方下载页](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/game-engine/rd-to-SCgame/BGDT-handbook/install)：2026-11-23 下载工具 3.0.271，安装包 SHA-256 为 `21D568AC3C6B1306F21F060FB036FD76913E6839312819D3C794637131F021B7`。

2026-11-23 通过 BGDT 3.0.271 的 cp 模式安装官方 TTSDK **6.9.7**，根目录为 `Assets/Plugins/ByteGame/com.bytedance.starksdk`。官方二进制仅保存在忽略的隔离测试工程，不随仓库分发。BGDT 版本不是 TTSDK 版本；不能以 TikTok 的同名 ttsdk.dll 代替抖音依赖。已生成 142 个文件的本地哈希快照，升级时重新生成并比较。

## 装配边界

只在抖音目标启用 ERDON_PLATFORM_DOUYIN。适配程序集面向 Editor/WebGL；本轮不实现抖音 Native 导出路径。清单同时登记 UPM 路径和上述 BGDT 实际安装路径，以及官方程序集。官方 ttsdk.dll 的 Any Platform 导入开关开启，不能只靠我们适配层的宏阻止它进入其他目标；发行工程必须隔离官方依赖，并通过构建审计。

抖音与 TikTok 官方 DLL 可能重名，发行工程只安装目标平台依赖，不同时导入二者。普通游戏业务不引用 TTSDK 命名空间。初始化层使用 DouyinCredentialProvider 组合 VerifiedAuth，SdkConfig.Platform 为 Douyin。账号 code 不写入日志。

## 验证状态

`tests/run-all-tests.ps1` 保留无官方依赖的替身回归。另运行以下命令，定义 OFFICIAL_DOUYIN 排除替身并直接引用 6.9.7 的真实 DLL，已通过适配器入口编译与注入回调测试（匿名拒绝、空 code、首次回调胜出、释放和脱敏）：

```powershell
./tests/run-douyin-tests.ps1 -OfficialPluginPath '<工程>/Assets/Plugins/ByteGame/com.bytedance.starksdk'
```

此测试不在独立进程调用真实 TT.Login，不证明运行时初始化或真机登录。完整 Unity 工程编译、后端及真机验证另行记录；允许非生产 Development Build 技术验证，正式/生产构建仍阻断。最小工程安装官方包还需 Unity 的 UGUI、AssetBundle 和 UnityWebRequest 等模块。

## 服务端兑换

server/src/douyin.mjs 根据[官方小游戏 code2Session 文档](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/game-engine/rd-to-SCgame/server/log-in/code-2-session)（2026-11-23 核对），固定向小游戏兑换接口发起 GET。请求携带受信服务端配置的应用身份及临时 code，不使用小程序 v2 或 TikTok OAuth 地址。客户端不能覆盖 URL 或密钥。

本地参考启动器可设置 DOUYIN_APP_ID、DOUYIN_APP_SECRET 与 SDK_GAME_ID；缺失部分配置会拒绝启动。WECHAT_* 变量可同时配置，两者按受信绑定选取验证器。密钥不得放入 Unity 配置或代码仓库。

验证器要求明确成功状态及非空账号身份/会话密钥；只有匿名身份或互相矛盾的成功/错误字段均拒绝。仅把验证后的账号标识交给 AuthService；平台 session_key 不下发客户端。应用/密钥/必要参数配置错误映射 AUTH.PLATFORM_CONFIGURATION，凭证拒绝映射 AUTH.PLATFORM_REJECTED，上游不可用映射 AUTH.PLATFORM_UNAVAILABLE；不输出任意平台错误正文。

五项模拟响应测试和全部服务端回归通过，不代表真实 code 兑换成功。真实凭证、平台授权及 Unity HTTPS 链路仍待联调。

完整工程证据：独立 Unity 2022.3.14f1c1 工程导入官方 BGDT/TTSDK，补齐内置模块及 UGUI，启用 ERDON_PLATFORM_DOUYIN 后成功生成并加载 ErDon.UnitySDK.Douyin.dll；日志 douyin-verify.log 输出 DOUYIN_UNITY_COMPILE_PASS，退出码 0。可把 tests/DouyinCompileCheck.cs.txt 复制为测试工程 Assets/Editor/DouyinCompileCheck.cs，先执行 Configure，重开 Unity 后执行 Verify。这里只验证 Editor 编译加载，不等于 WebGL 导出或真机可用。

## 字符串存储

`DouyinStorage(gameId, environment)` 实现 IStorage，使用本次官方包的 TT.PlayerPrefs（ttsdk.xml 中的 TTPlayerPrefs 文档与 DLL 入口已核对）。初始化官方运行时成功后，在工厂的 CreateStorage 中返回它：

```csharp
using ErDon.UnitySDK;

public IStorage CreateStorage()
{
    return new ErDon.UnitySDK.Douyin.DouyinStorage("my-game", "production");
}
```

通过 SDK.InitializeWithFactory / UnitySdkHost 的对应初始化入口装配，并设置 StorageEnabled=true。业务层随后只调用 SDK.SetStorageSync、SDK.GetStorageSync、SDK.RemoveStorageSync，见 [存储接口示例](storage.md)。关闭模块时工厂不创建它。

物理 key 包含转义后的 gameId/environment，避免同设备多个游戏、测试与生产环境串档。这里只隔离游戏/环境，不自动隔离玩家账号；有账号隔离需求的游戏应使用包含稳定游戏用户 ID 的逻辑 key。不在本地存储会话密钥或支付凭据。

写入/删除调用 Save 后回读检查；静默写失败返回 STORAGE.WRITE_FAILED，删除未生效返回 STORAGE.REMOVE_FAILED。缺失 key 返回 STORAGE.NOT_FOUND，已存在却返回 null 为 STORAGE.READ_FAILED，非 SDK 字符串格式为 STORAGE.FORMAT_MISMATCH。异常统一 STORAGE.PROVIDER_FAILED，不回传含数据的异常正文。Dispose 不删除存档。回读成功不证明断电重启后的持久性。

run-douyin-tests.ps1 已覆盖真实 DLL 入口编译和注入存储行为，包括空字符串、Unicode、作用域分隔符冲突、静默失败、格式异常、重复删除和生命周期；真机容量、宿主配额及重启持久性仍待验证。

## 分享

`DouyinShare` 实现 IShare，在工厂同时实现 IShareProviderFactory，并令 CreateShare 返回 new DouyinShare()。ShareEnabled=true 时才创建；业务调用与 [SDK.Share 示例](share.md) 相同。必须先完成官方 TT.InitSDK。

本次核对安装包 ttsdk.xml 及 6.9.7 DLL 的真实方法：TT.ShareAppMessage(JsonData, OnShareSuccessCallback, OnShareFailedCallback, OnShareCancelledCallback)。成功携带 Dictionary<string,object>，失败只有 string，取消无参数。此接口与 TikTok 的 ShareAppMessageParam 不同。程序集显式引用官方 TTLitJson；不得复制 TikTok DLL 或混装两个平台。

统一 Title/ImageUrl/Query 映射为 JSON 的 title/imageUrl/query，null 可选字段不发送。标题必填；业务负责分享图片、业务参数和平台审核配置。JSON 通过官方 JsonData 序列化，不手拼字符串。分享成功回调映射 Dispatched，不证明发送、送达或奖励条件；失败映射 SHARE.PLATFORM_REJECTED，平台取消映射 SHARE.CANCELLED，调用异常映射 SHARE.PROVIDER_FAILED。任意失败文本、成功字典都不写入日志或猜测数字错误码。首个回调生效，Dispose 后不派发旧结果；等待超时由聚合层管理。

官方 XML 所附 JS 参数说明链接为 https://microapp.bytedance.com/docs/zh-CN/mini-game/develop/api/retweet/tt-share-app-message/ 。当前网页读取未返回参数正文，因此未据此声称平台字段限制或真机行为已验收。

run-douyin-tests.ps1 使用官方 TTLitJson 源码与 Unity Mono 编译器，覆盖字段/转义、省略可选字段、成功/失败/取消竞争、释放和异常脱敏。官方源码存在 CS3021 注解警告；无适配层编译错误。独立 Unity 工程 douyin-share-verify.log 输出 DOUYIN_SHARE_UNITY_COMPILE_PASS，退出码 0。仍需真机确认分享面板、图片及参数的实际表现。

## SDK 内置平台工厂

游戏无需重复实现三个 Create 方法，使用 DouyinProviderFactory 即可。以下函数放在游戏的持久 MonoBehaviour 中，并且只在官方 TT.InitSDK 已成功后调用。需要安装可选 HTTP 传输包；地址与 appId 替换为实际受信配置，客户端不保存 AppSecret。

```csharp
void OnDouyinRuntimeReady()
{
    var factory = new ErDon.UnitySDK.Douyin.DouyinProviderFactory(
        "my-game", "production",
        () => new ErDon.UnitySDK.UnitySessionExchange(
            this, "https://your-api.example/v1/session",
            "my-game", "your-douyin-app-id", "production"));
    ErDon.UnitySDK.UnitySdkHost.InitializeWithFactory(
        new ErDon.UnitySDK.SdkConfig {
            Platform = "Douyin", LoginEnabled = true,
            StorageEnabled = true, ShareEnabled = true, PayEnabled = false
        }, factory);
}
```

工厂构造不调用平台、不创建后端通信对象。只有 LoginEnabled=true 才请求新的 ISessionExchange，并与 DouyinCredentialProvider 组成 VerifiedAuth。关闭登录时可以省略第三个参数；启用登录但缺少工厂或工厂返回 null 会拒绝初始化。每次工厂调用必须返回新 exchange，禁止捕获并重复返回同一个已被 SDK 接管的实例。

SDK 接管并释放创建出的能力，VerifiedAuth 负责释放 exchange；游戏不再重复 Dispose。游戏提供的协程宿主必须活到 SDK.Shutdown，建议在持久启动对象上装配。此工厂不处理 TTSDK 安装/初始化，也不实现 IPayProviderFactory，开启支付会被明确拒绝。配置面板和运行时 JSON 已支持按平台注册的自动启动装配，真实平台验收仍未完成。

## 官方运行时初始化桥接

新增 IPlatformRuntime 和 DouyinRuntime，独立于登录能力。证据来自 TTSDK 6.9.7 随包 ttsdk.xml 的 TT.InitSDK 说明及真实 DLL：回调为 (int code, ContainerEnv environment)，0 无错误，1 SDK 版本不支持，2 Unity 引擎版本不支持。此定义不自动套用于 TikTok 或微信。

```csharp
using ErDon.UnitySDK;
using ErDon.UnitySDK.Douyin;

// 只开启存储；启用登录时给工厂传入新的后端交换器构造函数。
UnitySdkHost.BeginStartup(
    new SdkConfig { Platform = "Douyin", LoginEnabled = false, StorageEnabled = true },
    new DouyinRuntime(),
    new DouyinProviderFactory("your-game-id", "test"),
    result => {
        if (!result.Success) {
            UnityEngine.Debug.LogError(result.Error.Code + " / " + result.Error.Reason + " callId=" + result.CallId);
            return;
        }
        // 此时再调用已开启的业务能力，启动不会自动登录。
    });
```

成功前 IsReady=false；成功后同一实例再次 Initialize 直接返回已就绪，不再次调用官方接口。并发调用返回 INIT.IN_PROGRESS；失败后仅允许调用方显式决定是否重新尝试，没有自动重试。1/2 映射 INIT.SDK_VERSION_UNSUPPORTED / INIT.ENGINE_VERSION_UNSUPPORTED，其他非零码映射 INIT.PLATFORM_FAILED 并保留数字 PlatformCode，调用异常映射 INIT.PROVIDER_EXCEPTION。

Dispose 只释放桥接自身并抑制迟到回调，不声称销毁或回滚官方 SDK。直接调用 runtime.Initialize 不具备超时或统一启动日志；上面的 UnitySdkHost.BeginStartup 已提供超时、CallId 和主线程驱动，并接入面板启动路径。所有宿主入口在主线程调用，唯一负责人退出时执行 UnitySdkHost.Shutdown。官方不回调时由启动编排超时拒绝继续业务。真机环境和 ContainerEnv 对不同宿主的意义仍待验收。

run-douyin-tests.ps1 验证实际方法入口编译与注入状态码，覆盖成功/版本失败/未知码、首次回调、并发拒绝、重复成功、释放后迟到回调和异常脱敏；不是实际宿主初始化测试。

## 可选网络通信工厂

运行时配置 network=true 时，向 UnitySdkHost.BeginConfiguredStartup 传入 `createNetwork: () => new UnityNetwork(this)`，其中 this 是游戏持久 MonoBehaviour。需要额外安装 com.erdon.unitysdk.transport.http；平台适配包本身不依赖该包。登录关闭可只验证网络启动路径。未传工厂返回 CONFIG.NETWORK_PROVIDER_REQUIRED；工厂直到平台就绪才会调用，SDK 负责释放实例。

本地 6.9.7 包包含 WebGL/Compatibility/UnityWebRequestCompatibility.cs，使用 UnityWebRequest 的兼容方法。本实现复用 UnityWebRequest，而不把其他平台的请求 DTO 套入 TTSDK。配置映射和平台身份/所有权测试已通过；具体小游戏网络栈的 HTTP 方法、取消、超时、响应头、重定向、二进制及接收限额仍需平台验证。TikTok 本地插件含定制的来源边界仍然适用。
