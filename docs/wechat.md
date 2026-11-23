# 微信登录适配（开发中）

当前实现是独立凭证适配包 `com.erdon.unitysdk.wechat`，不是已验收的完整微信发布方案。官方依赖固定为本机已核对的 `com.qq.weixin.minigame@0.1.23`，程序集 `Wx`，命名空间 `WeChatWASM`。官方 SDK 文件不随本仓库分发。

## 安装及边界

1. 从官方渠道取得对应版本 SDK，在项目 manifest 中显式配置官方包本地路径或团队私有包源；不能假定 Unity 默认 Registry 提供这个包。
2. 添加公共核心和微信适配 UPM 包。在微信目标设置 `ERDON_PLATFORM_WECHAT`，适配程序集只在 Editor/WebGL 编译。此宏不替代官方微信导出流程。
3. 官方微信运行时必须按其说明完成初始化，再请求登录。Editor 下不承诺官方 API 可用；预览继续用 Editor Mock。
4. 组合 `WeChatCredentialProvider`、自己的 `ISessionExchange`（或可选 HTTP 交换包）及 `VerifiedAuth`。上层业务仍然只调用 `SDK.Login`。

非微信项目不要安装微信适配包及其官方依赖。只去掉宏只能排除本适配程序集，不能移除官方 SDK 的插件和资源。构建审计清单登记了两者来源，但完整导出产物裁剪仍待验收。当前配置编辑器仍阻断真实平台发布，避免把凭证接口完成误标为端到端完成。

## 组合示例

下列代码位于项目微信专属程序集。`postToMainThread` 是初始化层提供的主线程队列；`sessionExchange` 是真正的服务端会话交换实现，不能把微信 code 当作游戏 session。

```csharp
IAuth auth = new VerifiedAuth(
    new ErDon.UnitySDK.WeChat.WeChatCredentialProvider(10000),
    sessionExchange,
    postToMainThread);
UnitySdkHost.Initialize(new SdkConfig {
    Platform = "WeChat",
    LoginEnabled = true
}, auth);

SDK.Login(result => {
    if (result.Success) {
        // 使用 result.Session 请求游戏服务端，不输出到日志。
        return;
    }
    // 用统一错误码显示提示，CallId 关联 SDK 日志。
    // AUTH.PLATFORM_FAILED 时 PlatformCode 保留微信 errno。
});
```

适配器超时参数控制微信授权请求；`SDK.Login` 默认 15 秒控制包含后端交换的整体等待。业务需要更长时间时显式传入整体 timeoutMs。取消整体等待并不撤销微信/服务端已经处理的请求。

## 错误与诊断

| 统一码 | 原因 | 处理 |
| --- | --- | --- |
| AUTH.PLATFORM_FAILED | 微信 fail 回调 | 检查 PlatformCode、官方错误码文档和网络环境；不自动重试 |
| AUTH.INVALID_CREDENTIAL | success 返回空 code | 检查官方 SDK/基础库兼容性 |
| AUTH.PLATFORM_EXCEPTION | 调用 WX.Login 同步抛异常 | 检查官方运行时初始化与目标环境 |
| SDK.DISPOSED | 对已释放的 provider 发起调用 | 重新初始化 SDK |

为防止平台任意错误字符串包含凭据，本适配器不保留原始 errMsg，只保留结构化 errno。没有明确可验证的错误映射时，Retry 保持 Unknown，不把未知失败猜成用户拒绝或限流。外层 VerifiedAuth 设置 PlatformAuthorization 阶段，SDK 关联 CallId。敏感 code 不写入日志。

## 验证与升级

```powershell
./tests/run-wechat-tests.ps1 -OfficialPackagePath '你的官方SDK目录'
```

该测试引用真实 0.1.23 官方 DTO DLL，替换 WX 调度入口，验证空凭据、首次回调胜出、原始消息脱敏、释放后回调、调用异常及超时传参。它不等价于完整官方运行时编译、微信开发者工具测试、服务端校验或真机验证。

升级先在独立项目修改固定版本，核对 LoginOption、success/fail 数据结构与导出依赖；更新上述测试版本断言、执行回归，再验证开发者工具及真机成功/失败/超时链路。全部验收后更新兼容矩阵与包版本。公共 IPlatformCredentialProvider 和 SDK.Login 不因官方版本更新而改变。

## 微信字符串存储

`WeChatStorage(gameId, environment)` 实现 IStorage，使用已核对的 WX.StorageSetStringSync/GetStringSync/HasKeySync/DeleteKeySync。官方运行时初始化后，在 SDK 初始化层传入此实例并启用 StorageEnabled。上层继续使用 [统一存储接口](storage.md)。

物理键前缀为 `erdon:v1:<转义gameId>:<转义environment>:`，值带 `erdon-string-v1:` 版本前缀。因此不同游戏与环境不共用键，空字符串不被误当作不存在，已有不兼容格式报 STORAGE.FORMAT_MISMATCH，不自动迁移或清空。Dispose 仅关闭实例，不擦除玩家数据；不提供清空整个微信应用存储的接口。

由于官方字符串帮助方法可能在错误时返回默认值，写入后回读并比较，删除后检查键是否仍存在：未保留值返回 STORAGE.WRITE_FAILED，未删除返回 STORAGE.REMOVE_FAILED；已存在键读取 null 返回 STORAGE.READ_FAILED。HasKey 本身不能区分官方吞掉的底层错误与不存在，此限制需真机确认，不能声称已覆盖所有配额/磁盘错误。同步回读增加一次访问，实际性能及配额限制待测。

运行 `tests/run-wechat-storage-tests.ps1 -CompiledWxAssembly '<已导入官方SDK的Unity工程>/Library/ScriptAssemblies/Wx.dll'` 验证真实编译入口与注入存储行为。当前证据来自本机已有工程编译出的 Wx.dll，不是干净工程重建或真机持久化验收，完整平台构建仍不放行。


## 分享

WeChatShare 实现 IShare。在官方微信运行时初始化后，通过 UnitySdkHost.Initialize 的 shareProvider 参数或 IShareProviderFactory.CreateShare 创建它，并启用 SdkConfig.ShareEnabled。上层调用与失败示例见 [分享接口](share.md)。

映射到 WX.ShareAppMessage(ShareAppMessageOption)：Title→title、ImageUrl→imageUrl、Query→query，query 原样传入，业务先编码；不自动补图或改变参数。可选图片为空时交给宿主默认行为。当前公共契约不提供 imageUrlId、path、toCurrentGroup 等微信专有扩展。

核对的本地 0.1.23 DTO 没有成功/失败回调；调用正常返回即报告 Dispatched，不表示用户发送、取消或对方收到。底层抛异常返回 SHARE.PROVIDER_FAILED，原始异常不泄露；释放后调用返回 SDK.DISPOSED。实际宿主异步失败无法由该入口推断。

run-wechat-share-tests.ps1 需要 OfficialPackagePath 与 CompiledWxAssembly，引用官方 DTO 和现有工程 Wx.dll 编译入口，注入 dispatch 验证字段、可选参数、非法参数、异常、释放及回调异常不重复调用。当前测试输出 WECHAT_SHARE_TESTS_PASS，但编译报告其他类型的 CS1684 警告，表明现有工程编译产物和 0.1.23 DTO 包不完全匹配。此证据仅限分享所用入口，不是干净官方工程编译或整包兼容通过。后续须从一致来源重新编译并完成真机分享。
## SDK 内置平台工厂

游戏无需重复实现三个 Create 方法，使用 WeChatProviderFactory 即可。以下函数放在游戏的持久 MonoBehaviour 中，并且只在官方 WX.InitSDK 已成功后调用。需要安装可选 HTTP 传输包；地址与 appId 替换为实际受信配置，客户端不保存 AppSecret。

```csharp
void OnWeChatRuntimeReady()
{
    var factory = new ErDon.UnitySDK.WeChat.WeChatProviderFactory(
        "my-game", "production",
        () => new ErDon.UnitySDK.UnitySessionExchange(
            this, "https://your-api.example/v1/session",
            "my-game", "your-wechat-app-id", "production"));
    ErDon.UnitySDK.UnitySdkHost.InitializeWithFactory(
        new ErDon.UnitySDK.SdkConfig {
            Platform = "WeChat", LoginEnabled = true,
            StorageEnabled = true, ShareEnabled = true, PayEnabled = false
        }, factory);
}
```

工厂构造不调用平台、不创建后端通信对象。只有 LoginEnabled=true 才请求新的 ISessionExchange，并与 WeChatCredentialProvider 组成 VerifiedAuth。关闭登录时可以省略第三个参数；启用登录但缺少工厂或工厂返回 null 会拒绝初始化。每次工厂调用必须返回新 exchange，禁止捕获并重复返回同一个已被 SDK 接管的实例。

SDK 接管并释放创建出的能力，VerifiedAuth 负责释放 exchange；游戏不再重复 Dispose。游戏提供的协程宿主必须活到 SDK.Shutdown，建议在持久启动对象上装配。此工厂不处理 微信 SDK 安装/初始化，也不实现 IPayProviderFactory，开启支付会被明确拒绝。配置面板到正式平台的自动启动装配仍待完成。

工厂验证命令：tests/run-wechat-factory-tests.ps1 -OfficialPackagePath <0.1.23包目录> -CompiledWxAssembly <Wx.dll路径>。配置及释放测试通过；本机 Wx.dll 与包 DTO 仍存在其他接口类型 CS1684 警告，不能作为干净官方版本组合验收。

## 0.1.23 隔离工程复验（2026-11-23）

用本地 LocalPackages 的 0.1.23 包建立独立 WeChatCompileProject，通过 UPM file 依赖加载该包及本仓库 core/wechat；未复用旧游戏 Library。启用 ERDON_PLATFORM_WECHAT 后，官方源码和适配程序集编译通过，wechat-clean-verify.log 输出 WECHAT_SHARE_UNITY_COMPILE_PASS，退出码 0。该检查确认登录、存储、分享类型加载。测试入口保存在 tests/WeChatCompileCheck.cs.txt，先 Configure，再重新启动执行 Verify。

将测试的 CompiledWxAssembly 改为这个隔离工程生成的 Library/ScriptAssemblies/Wx.dll 后，工厂和分享测试均通过，原 CS1684 类型不匹配警告消失。旧游戏的 Wx.dll 不应继续作为 0.1.23 的验证依赖。工厂/分享脚本使用 Unity Mono 运行，支持通过 -UnityMono 指定安装位置。

上述验证说明本地这份包可以重新编译，不能证明本地文件从未修改。已生成 334 个文件的哈希快照，官方来源完整性、目标导出与真机仍需单独验证。此前的警告记录属于旧程序集组合，不代表此次复验结果。

## 官方初始化桥接（0.1.23）

`WeChatRuntime` 实现 IPlatformRuntime，调用 WX.InitSDK；仅回调码 200 表示就绪，0 以及其他返回码均映射 INIT.PLATFORM_FAILED，PlatformCode 保留原始数字。依据是本地包 `Runtime/wechat-default/unity-sdk/index.js` 的 WXInitializeSDK：moduleHelper.init 后发送 Inited=200；WXSDKManagerHandler.Inited 将整数直接交给 Action<int>。升级 SDK 时需重新核对这条契约。

```csharp
// 示例只开存储；开启登录时需给工厂提供后端 ISessionExchange 工厂。
UnitySdkHost.BeginStartup(
    new SdkConfig { Platform = "WeChat", LoginEnabled = false, StorageEnabled = true },
    new WeChatRuntime(),
    new WeChatProviderFactory("your-game-id", "test"),
    result => {
        if (!result.Success) {
            UnityEngine.Debug.LogError(result.Error.Code + " platformCode=" + result.Error.PlatformCode);
            return;
        }
        SDK.SetStorageSync("example", "ready");
    });
```

初始化期间再次调用同一桥接返回 INIT.IN_PROGRESS；已就绪重复初始化不再调用官方入口。失败后可以显式重试；释放后拒绝调用，忽略迟到结果。桥接本身不计时，BeginStartup 默认等待 15 秒并由宿主驱动；不要从多个桥接或其他插件同时调用 WX.InitSDK，因为本地官方实现保存单个初始化回调。

Dispose 只结束桥接生命周期，不卸载微信官方 SDK。该实现已用官方程序集编译与注入返回码验证，不代表真机初始化、账号授权或后端联调完成。官方包来源完整性仍需独立核验。

## 网络通信配置

本地 0.1.23 的 C# 接口没有通用 WX.Request/RequestOption；Unity 网络走 UnityWebRequest。随包 CHANGELOG.md 第 336 行记录 UnityWebRequest timeout 支持，第 71 行记录请求头缓存控制。当前沿用可选 HTTP 包的 UnityNetwork，不虚构独立的 WX.Request C# 签名。

```csharp
// 位于游戏 MonoBehaviour 的 Awake/Start 中；需安装 com.erdon.unitysdk.transport.http。
var data = new SdkRuntimeConfiguration {
    platform = "WeChat", gameId = "your-game", appId = "your-app",
    login = false, network = true
};
UnitySdkHost.BeginConfiguredStartup(data, null, result => {
    if (!result.Success) return;
    // 此后可调用 SDK.Request。
}, createNetwork: () => new UnityNetwork(this));
```

工厂仅在平台就绪且 network=true 时调用，应每次创建新 provider。SDK 负责其释放，宿主应覆盖 SDK 使用周期；销毁宿主前调用 UnitySdkHost.Shutdown。省略网络工厂返回 CONFIG.NETWORK_PROVIDER_REQUIRED，预检不调用平台初始化。关闭网络时即使提供工厂也不会创建它。

面板使用同一路径：在 Editor Play Mode 的游戏代码中设置 `EditorPlatformBootstrap.NetworkFactory = () => new UnityNetwork(this)`（包裹 UNITY_EDITOR 条件编译），选择微信并勾选网络后启动。退出 Play 会清除工厂引用。核心和微信适配包不依赖 HTTP 包，未使用网络的游戏无需安装它。

该接线已通过公共所有权/开关测试和 Unity 编译，尚未证明微信导出网络桥接的域名白名单、响应头、二进制响应、超时、取消、重定向和分块限额行为。正式构建仍阻断，后续需在微信开发者工具和真机验收。
