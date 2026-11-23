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

此测试不在独立进程调用真实 TT.Login，不证明运行时初始化或真机登录。完整 Unity 工程编译、后端及真机验证另行记录；真实平台构建保持阻断。最小工程安装官方包还需 Unity 的 UGUI、AssetBundle 和 UnityWebRequest 等模块。

## 服务端兑换

server/src/douyin.mjs 根据[官方小游戏 code2Session 文档](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/game-engine/rd-to-SCgame/server/log-in/code-2-session)（2026-11-23 核对），固定向小游戏兑换接口发起 GET。请求携带受信服务端配置的应用身份及临时 code，不使用小程序 v2 或 TikTok OAuth 地址。客户端不能覆盖 URL 或密钥。

本地参考启动器可设置 DOUYIN_APP_ID、DOUYIN_APP_SECRET 与 SDK_GAME_ID；缺失部分配置会拒绝启动。WECHAT_* 变量可同时配置，两者按受信绑定选取验证器。密钥不得放入 Unity 配置或代码仓库。

验证器要求明确成功状态及非空账号身份/会话密钥；只有匿名身份或互相矛盾的成功/错误字段均拒绝。仅把验证后的账号标识交给 AuthService；平台 session_key 不下发客户端。应用/密钥/必要参数配置错误映射 AUTH.PLATFORM_CONFIGURATION，凭证拒绝映射 AUTH.PLATFORM_REJECTED，上游不可用映射 AUTH.PLATFORM_UNAVAILABLE；不输出任意平台错误正文。

五项模拟响应测试和全部服务端回归通过，不代表真实 code 兑换成功。真实凭证、平台授权及 Unity HTTPS 链路仍待联调。

完整工程证据：独立 Unity 2022.3.14f1c1 工程导入官方 BGDT/TTSDK，补齐内置模块及 UGUI，启用 ERDON_PLATFORM_DOUYIN 后成功生成并加载 ErDon.UnitySDK.Douyin.dll；日志 douyin-verify.log 输出 DOUYIN_UNITY_COMPILE_PASS，退出码 0。可把 tests/DouyinCompileCheck.cs.txt 复制为测试工程 Assets/Editor/DouyinCompileCheck.cs，先执行 Configure，重开 Unity 后执行 Verify。这里只验证 Editor 编译加载，不等于 WebGL 导出或真机可用。

## 字符串存储

`DouyinStorage(gameId, environment)` 实现 IStorage，使用本次官方包的 TT.PlayerPrefs（ttsdk.xml 中的 TTPlayerPrefs 文档与 DLL 入口已核对）。初始化官方运行时成功后，在工厂的 CreateStorage 中返回它：

```csharp
public IStorage CreateStorage()
{
    return new ErDon.UnitySDK.Douyin.DouyinStorage("my-game", "production");
}
```

通过 SDK.InitializeWithFactory / UnitySdkHost 的对应初始化入口装配，并设置 StorageEnabled=true。业务层随后只调用 SDK.SetStorageSync、SDK.GetStorageSync、SDK.RemoveStorageSync，见 [存储接口示例](storage.md)。关闭模块时工厂不创建它。

物理 key 包含转义后的 gameId/environment，避免同设备多个游戏、测试与生产环境串档。这里只隔离游戏/环境，不自动隔离玩家账号；有账号隔离需求的游戏应使用包含稳定游戏用户 ID 的逻辑 key。不在本地存储会话密钥或支付凭据。

写入/删除调用 Save 后回读检查；静默写失败返回 STORAGE.WRITE_FAILED，删除未生效返回 STORAGE.REMOVE_FAILED。缺失 key 返回 STORAGE.NOT_FOUND，已存在却返回 null 为 STORAGE.READ_FAILED，非 SDK 字符串格式为 STORAGE.FORMAT_MISMATCH。异常统一 STORAGE.PROVIDER_FAILED，不回传含数据的异常正文。Dispose 不删除存档。回读成功不证明断电重启后的持久性。

run-douyin-tests.ps1 已覆盖真实 DLL 入口编译和注入存储行为，包括空字符串、Unicode、作用域分隔符冲突、静默失败、格式异常、重复删除和生命周期；真机容量、宿主配额及重启持久性仍待验证。
