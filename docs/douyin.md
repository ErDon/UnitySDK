# 抖音适配（开发中）

独立 UPM 包 com.erdon.unitysdk.douyin 已实现 DouyinCredentialProvider。上层 SDK.Login 不变，下层强制请求已登录账号；isLogin=false 返回 AUTH.NOT_LOGGED_IN，空 code 返回 AUTH.INVALID_CREDENTIAL，匿名 code 不作为游戏账号凭据。成功取得 code 后仍必须经 VerifiedAuth 和抖音服务端验证器交换会话。后端抖音验证器尚未实现。

## 官方接入来源

- [账号接口](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/api/c-api/open-ability/account)：抖音登录回调包含 code、anonymousCode、isLogin，与 TikTok 的单 code 回调不同。
- [安装 TTSDK](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/game-engine/rd-to-SCgame/unity-game-access/install-connect/install-sdk)：通过 BGDT 登录并安装 TTSDK。
- [BGDT 官方下载页](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/game-engine/rd-to-SCgame/BGDT-handbook/install)：2026-11-23 下载工具 3.0.271，安装包 SHA-256 为 `21D568AC3C6B1306F21F060FB036FD76913E6839312819D3C794637131F021B7`。

官方安装包保存在本地忽略的 .tests/vendor，导入独立 .tests/DouyinProject；官方二进制不随仓库分发。BGDT 版本不是 TTSDK 版本，尚未完成 TTSDK 精确版本锁定。不能以 TikTok 的同名 ttsdk.dll 代替抖音依赖。

## 装配边界

只在抖音目标启用 ERDON_PLATFORM_DOUYIN。适配程序集面向 Editor/WebGL；本轮不实现抖音 Native 导出路径。安装官方包后必须确认其 PluginImporter 和版本，并更新依赖审计路径。当前清单登记 Packages/com.bytedance.starksdk，若 BGDT 使用其他目录应同步登记实际根目录。

抖音与 TikTok 官方 DLL 可能重名，发行工程只安装目标平台依赖，不同时导入二者。普通游戏业务不引用 TTSDK 命名空间。初始化层使用 DouyinCredentialProvider 组合 VerifiedAuth，SdkConfig.Platform 为 Douyin。账号 code 不写入日志。

## 验证状态

tests/run-all-tests.ps1 中的 DouyinCredentialTests 使用官方文档形状的测试替身，验证匿名拒绝、空 code、首次回调胜出、释放和脱敏。它不证明官方 DLL 编译、运行时初始化或真机登录。官方安装、精确依赖版本、干净工程编译、后端及真机验证仍待完成，真实平台构建保持阻断。
