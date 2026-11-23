# TikTok 登录适配（开发中）

独立包 `com.erdon.unitysdk.tiktok` 提供 `TikTokCredentialProvider`。它调用官方 `TT.Login` 获取临时 code，必须组合 `VerifiedAuth` 和后端 `ISessionExchange` 才能获得游戏会话。上层仍调用 `SDK.Login`。

## 官方依赖与安装

当前核对基线：TikTok Unity SDK **1.1.6 Release**。本机插件 package.json 的 version 为 `1.1.6-Release`，name 却仍为 `com.bytedance.starksdk`，因此不能仅通过包名区分抖音与 TikTok。此适配包不声明这个易混淆的 UPM 自动依赖；需要从 TikTok 官方渠道导入正确插件，并由发布工程锁定安装包及校验值。后续依赖装配器仍待实现。

- 按官方流程安装到 `Assets/Plugins/com.tiktok.minigame`；迁移路径时必须同步登记审计清单，否则无法完整审计该插件。
- 微信或抖音发行工程不要安装 TikTok 官方插件，也不要同时导入同名 TTSDK 二进制。
- 只在 TikTok 目标启用 `ERDON_PLATFORM_TIKTOK`；适配程序集支持 Editor/WebGL。
- 官方插件分别为 Editor 导入 `ttsdk-editor.dll`、WebGL 导入 `ttsdk.dll`。程序集依照官方 PluginImporter 自动引用当前目标的二进制，不能强制 Editor 引用 WebGL DLL。
- 本机旧游戏插件的导入设置含 `RS_TIKTOK`，这属于旧项目配置，不是本 SDK 依赖。请使用官方干净插件，不复制旧游戏的宏和导入设置。
- 必须等 `TT.InitSDK` 成功（返回 0）后再初始化本登录组合并调用登录。Editor Mock code 不是真实凭证。

## 干净官方包验证记录（2026-11-23）

已从上面的官方 Unity 构建文档所链接的 [1.1.6 Release 安装包](https://sf16-sg.tiktokcdn.com/obj/eden-sg/wv_k4aa/ljhwZthlaukjlkulzlp/mini_games/unity/com.tiktok.minigame%401.1.6-Release.unitypackage) 下载完整包。包内 name 同样为 com.bytedance.starksdk，不能据此将其当作抖音包，也不能用 version 字段单独认证来源。

本次下载的 SHA-256（用于复现该基线，不是官方签名）：

| 文件 | SHA-256 |
| --- | --- |
| 完整 unitypackage | `9d7154f1e89180fc6b9e76704362d72b37e9c78a5d59257b26f24700ffcc4f35` |
| TTSDK/ttsdk.dll | `35a7dbb619b812d58abf6e33bb3cb05d9e7f77ad0bb1131c898b78d4f38d947d` |
| TTSDK/ttsdk-editor.dll | `3c9ca4a76ea625d6445f25daaa3679ac9b5bba69ea69e0e35759b21351c645f1` |

对这份完整包运行 run-tiktok-tests.ps1，Runtime/Editor 两套 DLL 的七组签名和注入行为测试均通过。新建 TikTokBuildProject 使用完整原始插件，未移入旧游戏宏或修改其导入配置，Unity 2022.3.14f1c1 初次导入和 ConfigureTikTok 正常退出。随后 WebGL Development Build 得到 Succeeded errors=0，链接后 TikTok 适配器及注册方法检查通过；空场景 Play Mode 注册解析也通过，未调用平台 API。官方命令行小游戏导出也已成功退出 0，生成文件检查与 JSON 解析通过；操作和分包验证边界见 [开发构建](development-build.md)。真实平台初始化、功能和最终包体隔离必须分别验证，不能由上述测试代替。

2026-11-24 复核：独立工程中的 104 个官方插件文件与原安装包逐文件一致（含源码、DLL 与导入设置），未发现添加、删除或修改；双 DLL 七组契约回归再次通过。完整哈希基线见 [官方包清单](../compatibility/tiktok-official-1.1.6.inventory.json)。该核对证明本次工程使用的插件内容与所下载基线一致，不代表任意未来下载或真机功能均已认证。

## 用法

初始化层按 [微信组合示例](wechat.md) 的方式组合，只替换 provider 为：

```csharp
new ErDon.UnitySDK.TikTok.TikTokCredentialProvider()
```

同时把 `SdkConfig.Platform` 设为 `TikTok`。服务端根据凭证 Platform 选择 TikTok 验证器，不能交给微信 code 换 session 的接口。客户端不能包含 Client Secret。

统一错误：空 code → `AUTH.INVALID_CREDENTIAL`；官方 fail → `AUTH.PLATFORM_FAILED`；同步调用异常 → `AUTH.PLATFORM_EXCEPTION`；释放后调用 → `SDK.DISPOSED`。官方此接口只有字符串错误消息，没有结构化错误码，本实现不从任意字符串猜测 errno、限流等待时间或是否可重试，也不把可能含凭据的原文送给日志。整体超时、取消由 `SDK.Login`/`SDK.CancelLogin` 管理。

## 验证与限制

```powershell
./tests/run-tiktok-tests.ps1 -OfficialPluginPath '你的官方插件目录'
```

分别针对本机官方 Runtime/Editor DLL 编译真实 `TT.Login` 调用签名；行为测试注入调度函数，覆盖重复回调、空 code、释放、异常和敏感错误字符串。没有调用真实 TikTok 容器，不证明账号登录、网络、后端或真机可用。Player 发布继续被配置检查阻断。

官方参考（2026-11-23 核对）：

- [Unity 构建与初始化](https://developers.tiktok.com/docs/en/build-mini-games-with-unity)
- [登录与授权](https://developers.tiktok.com/docs/en/unity-sdk-login-and-authorization)
- [运行环境与登录排错](https://developers.tiktok.com/docs/en/mini-games-development-troubleshooting)

升级先核对 Runtime/Editor 两侧方法签名和导入目标，再回归测试，并完成真实容器、后端及真机验证后更新兼容矩阵。不得仅替换 DLL 就标记升级验收完成。

服务端身份兑换现已实现于 `server/src/tiktok.mjs`，依据 [Minis OAuth](https://developers.tiktok.com/docs/en/minis-oauth)。参见 [服务端接入](../server/README.md) 的绑定配置及令牌保存限制；当前仅通过模拟上游测试，尚未完成真实登录联调。

## 字符串存储

`TikTokStorage(gameId, environment)` 实现 IStorage。初始化层在官方 TT.InitSDK 成功后构造它，并通过 UnitySdkHost.Initialize 的 storageProvider 参数传入，同时设置 StorageEnabled=true。上层仍调用统一的 SetStorageSync/GetStorageSync/RemoveStorageSync。

底层调用 TT.PlayerPrefs.SetString/GetString/HasKey/DeleteKey，写入和删除后调用 Save。物理键使用 `erdon:v1:<转义gameId>:<转义environment>:` 前缀，值带 `erdon-string-v1:`；不会读写其他命名空间，也不会在 Dispose 时清空存档。若改变 gameId/environment，看到的是另一个命名空间，旧存档不会自动迁移。

写入后回读不一致返回 STORAGE.WRITE_FAILED；键存在但读取 null 返回 STORAGE.READ_FAILED；格式不兼容返回 STORAGE.FORMAT_MISMATCH；删除后键仍存在返回 STORAGE.REMOVE_FAILED。底层抛异常转换为 STORAGE.PROVIDER_FAILED，不泄露原文。键不存在返回 STORAGE.NOT_FOUND，空字符串仍是合法值。

上述 run-tiktok-tests.ps1 同时编译两套官方 DLL 的存储入口并运行注入行为测试。真实 PlayerPrefs、Save 落盘、配额、宿主退出及设备重启仍需真机验证；回读成功不能作为磁盘持久化证明。当前平台构建仍不放行。


## 分享

TikTokShare 实现 IShare，在 TT.InitSDK 成功后创建，通过 shareProvider 或 IShareProviderFactory.CreateShare 交给 SDK，启用 ShareEnabled。通用示例见 [分享能力](share.md)。

根据 [官方 Sharing 文档](https://developers.tiktok.com/docs/en/unity-sdk-sharing) 与本地 1.1.6-Release DLL，Title/ImageUrl/Query 映射到 ShareAppMessageParam 同名字段，等待官方 Success/Fail；不把调用函数返回或 Complete 当作成功。平台 Success 映射为 Dispatched，仍不证明好友收到。失败返回 SHARE.PLATFORM_REJECTED 并保留 ErrorInfo.ErrorCode 的数字字符串，ErrMsg 不传播；未知码不推断取消或地区原因。公共层负责等待超时、取消和主线程派发。

当前通用请求不提供 TikTok 专属 Subtitle、TemplateType、Path 配置，这些保留官方参数默认值。需要这些扩展的项目暂不宣称支持对应模板；须按平台文档补充显式能力后接入。

测试脚本 run-tiktok-tests.ps1 分别对 Runtime/Editor DLL 编译并运行注入测试，覆盖参数、成功/失败顺序、结构化错误、空错误、释放与异常。ErrorInfo 由官方内部创建，测试使用未初始化对象仅构造回调数据，不运行真实宿主。分享回调类型依赖 netstandard 2.1，使用 -UnityMono 指定 Unity 自带 Mono；旧 .NET Framework 运行器不能完整加载该类型。默认路径以本机 Unity 2022.3.14f1c1 为基线，可按安装路径覆盖。

两套 DLL 均输出 TIKTOK_SHARE_TESTS_PASS，原登录/存储和公共回归亦通过。没有真实 TikTok 容器或设备分享证据，发布仍不放行。
## SDK 内置平台工厂

游戏无需重复实现三个 Create 方法，使用 TikTokProviderFactory 即可。以下函数放在游戏的持久 MonoBehaviour 中，并且只在官方 TT.InitSDK 已成功后调用。需要安装可选 HTTP 传输包；地址与 appId 替换为实际受信配置，客户端不保存 AppSecret。

```csharp
void OnTikTokRuntimeReady()
{
    var factory = new ErDon.UnitySDK.TikTok.TikTokProviderFactory(
        "my-game", "production",
        () => new ErDon.UnitySDK.UnitySessionExchange(
            this, "https://your-api.example/v1/session",
            "my-game", "your-tiktok-app-id", "production"));
    ErDon.UnitySDK.UnitySdkHost.InitializeWithFactory(
        new ErDon.UnitySDK.SdkConfig {
            Platform = "TikTok", LoginEnabled = true,
            StorageEnabled = true, ShareEnabled = true, PayEnabled = false
        }, factory);
}
```

工厂构造不调用平台、不创建后端通信对象。只有 LoginEnabled=true 才请求新的 ISessionExchange，并与 TikTokCredentialProvider 组成 VerifiedAuth。关闭登录时可以省略第三个参数；启用登录但缺少工厂或工厂返回 null 会拒绝初始化。每次工厂调用必须返回新 exchange，禁止捕获并重复返回同一个已被 SDK 接管的实例。

SDK 接管并释放创建出的能力，VerifiedAuth 负责释放 exchange；游戏不再重复 Dispose。游戏提供的协程宿主必须活到 SDK.Shutdown，建议在持久启动对象上装配。此工厂不处理 TTSDK 安装/初始化，也不实现 IPayProviderFactory，开启支付会被明确拒绝。配置驱动启动已通过平台注册表接入，见 startup.md；真实宿主初始化和正式发布验收仍未完成。

工厂验证随 run-tiktok-tests.ps1 执行，已分别引用 1.1.6-Release 的 Runtime/Editor DLL 通过配置、缺失后端拒绝和释放测试，不代表真机初始化成功。

## 初始化桥接

`TikTokRuntime` 调用 TT.InitSDK，等待 code=0 才设置 IsReady，其他整数统一返回 INIT.PLATFORM_FAILED，并保留 PlatformCode。不将抖音的 1/2 错误原因套用到 TikTok；也不接受微信的成功码 200。

```csharp
UnitySdkHost.BeginStartup(
    new SdkConfig { Platform = "TikTok", LoginEnabled = false, StorageEnabled = true },
    new TikTokRuntime(),
    new TikTokProviderFactory("your-game-id", "test"),
    result => {
        if (!result.Success) {
            UnityEngine.Debug.LogError(result.Error.Code + " platformCode=" + result.Error.PlatformCode);
            return;
        }
        SDK.SetStorageSync("example", "ready");
    });
```

上例只启用存储。启用登录时需注入后端 ISessionExchange 工厂。桥接初始化未完成时返回 INIT.IN_PROGRESS，已就绪重复调用直接成功；失败可显式重试，释放后忽略迟到回调。BeginStartup 负责默认 15 秒本地超时，取消/释放不卸载平台 SDK、不保证中断官方初始化。初始化入口应由一个启动所有者管理。

最初的成功码核对使用本地 1.1.6-Release 的 Runtime/Editor DLL：TT.InitSDK 转发到 TTSDKInner，成功路径 CompleteClaimedInitialization 调用 TTSDKInitCompletionGate.TrySucceed，将整数 0 写入任务结果，InvokeInitCallbackAsync 转发该结果。此为二进制契约核对，不替代真机验证。后续下载的干净官方包两套 DLL 哈希与下列值一致，并已重新运行回归；旧游戏目录的其他定制文件不因此被认可。记录 SHA-256 便于后续升级对比：

- ttsdk.dll：`35A7DBB619B812D58ABF6E33BB3CB05D9E7F77AD0BB1131C898B78D4F38D947D`
- ttsdk-editor.dll：`3C9CA4A76EA625D6445F25DAAA3679AC9B5BBA69EA69E0E35759B21351C645F1`

`tests/run-tiktok-tests.ps1` 包含两套 DLL 的初始化签名编译和注入返回码测试。干净插件工程加载、开发构建与官方导出已验证；真实客户端初始化仍待验收。

## 可选网络通信工厂

运行时配置 network=true 时，向 UnitySdkHost.BeginConfiguredStartup 传入 `createNetwork: () => new UnityNetwork(this)`，其中 this 是游戏持久 MonoBehaviour。需要额外安装 com.erdon.unitysdk.transport.http；平台适配包本身不依赖该包。登录关闭可只验证网络启动路径。未传工厂返回 CONFIG.NETWORK_PROVIDER_REQUIRED；工厂直到平台就绪才会调用，SDK 负责释放实例。

本地 1.1.6-Release 包包含 WebGL/Compatibility/UnityWebRequestCompatibility.cs，使用 UnityWebRequest 的兼容方法。本实现复用 UnityWebRequest，而不把其他平台的请求 DTO 套入 TTSDK。配置映射和平台身份/所有权测试已通过；具体小游戏网络栈的 HTTP 方法、取消、超时、响应头、重定向、二进制及接收限额仍需平台验证。后续工程验证使用上文完整官方原件，不沿用旧游戏目录中的其他定制。
