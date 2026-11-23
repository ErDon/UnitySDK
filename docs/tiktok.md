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
