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
