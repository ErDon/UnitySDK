# 版本与验证矩阵

记录日期：2026-11-24。版本锁定不等于兼容性已验收；没有对应证据的格子一律为待验证。当前不能发布稳定版本。

| 组件 | 版本基线 | 当前证据 | 尚未完成 |
| --- | --- | --- | --- |
| Unity 核心/Editor/HTTP | SDK 0.1.0，Unity 2022.3.14f1c1 | 独立工程编译、Editor/Play Mode 检查、域重载关闭生命周期、HTTPS 请求与限额、三平台开发构建 | 多游戏完整验收、真实平台行为及最终产物隔离；面板已列必测交互通过，见 docs/panel-acceptance.md |
| 微信本地基线 | com.qq.weixin.minigame 0.1.23 | 隔离重编译、能力契约回归及 Unity WebGL Development Build | 包缺少转换性能库；来源完整性、官方转换、后端与真机 |
| 微信官方固定提交 | package 0.1.1；915d4bf4f60ef0cd1c29316a8d67257185d609a7 | 干净官方 Git 克隆；独立副本编译/官方小游戏转换；链接后微信自动注册保留；登录、工厂、存储、分享、广告、初始化与字符串上报八项契约回归（包含支付网关）；七模块全部关闭的自有实现链接检查 | 须显式 ERDON_WECHAT_STRING_ANALYTICS，数字上报拒绝；最终平台/模块裁剪、真机及真实服务端联调 |
| TikTok | 官方完整 1.1.6-Release；哈希见 docs/tiktok.md | 干净插件工程、Runtime/Editor 回归（含支付网关/工厂）、Play Mode 注册、开发构建/官方导出及 StreamingAssets 保留；七模块全部关闭的自有实现链接检查 | 公共契约与官方内部代码的最终保留范围、JS/Wasm/资源隔离、真机与后端联调 |
| 抖音 | TTSDK 6.9.7 | BGDT 官方安装、八组接口/注入回归（含支付网关/工厂）、独立开发构建与官方导出、注册保留；七模块全部关闭的自有实现链接检查 | 最终产物隔离、真实初始化、后端联调及真机 |
| BGDT 安装工具 | 3.0.271 | 官网安装包下载、独立 Unity 导入、cp 模式安装 TTSDK 6.9.7 | 新版本升级与真机回归 |
| 公共服务端 | 0.1.0，Node 24.20.0 | 115 项测试：模拟三平台上游、本机 HTTP/SQLite、会话/订单隔离、抖音/TikTok 支付通知与发货、TikTok 加密令牌续期、三平台配置与统一启动 | 官方接口调用、Unity HTTPS、部署、备份与性能 |
| Android 原生/Unity 桥接 | SDK 0.1.0，Java 8，API 26+；编译 API 33 | Java 核心回归、真实 Android API/AAR 与原生示例编译、Unity 隔离桥接检查、C# Player 全开/全关编译；AAR 哈希见 android/build-manifest.json | 真机、IL2CPP/JNI/R8、供应商与最终 APK/AAB 隔离 |
| iOS 原生渠道 | 无 | 扩展边界设计 | 本轮不接具体供应商 |
| Godot | 无 | 协议边界设计 | 本轮不实现引擎桥接 |

## 可重跑证据

- `python android/test.py`、`python android/build.py --android-jar <android.jar>`：原生行为与 AAR/原生示例构建。
- `tests/AndroidBridgeUnityChecks.cs`、`tests/compile-android-bridge.ps1`：隔离 Unity 桥接验证、Player 分支编译。

- `tests/run-all-tests.ps1`：纯 C# 核心、配置行为契约、存储和隔离策略。
- `tests/run-wechat-tests.ps1`：需本地官方 0.1.23 包；不是实际微信登录。
- `tests/run-wechat-storage-tests.ps1`：需工程编译的 Wx.dll；不是干净包重编译。
- `tests/run-tiktok-tests.ps1`：需本地 1.1.6-Release 插件；分别编译 Runtime/Editor 入口，当前已对干净官方下载包重跑。
- `tests/run-douyin-tests.ps1`：需 BGDT 安装的官方 TTSDK 6.9.7；真实 DLL 编译，回调为注入，不是真机登录。
- `tests/SdkEditorChecks.cs.txt`、`tests/SdkPlayChecks.cs.txt`：复制到独立 Unity 测试工程后执行。
- `server/` 下 `node --test`：包含临时本地端口和数据库，不访问真实平台。

## 升级记录

升级前运行 `tools/export-sdk-inventory.ps1 -Platform WeChat -PackageRoot '<官方包目录>' -OutputPath '<快照文件.json>'`，保存所测版本全部文件的 SHA-256（包括程序集、源码、导入设置、JS、Wasm、配置和资源）。输出不包含安装目录、密钥或任意包描述字段。它只证明被测试本地文件的内容，不能证明官方来源；本机已经改过宏的包也会如实记录。

升级后比较相对文件路径与哈希，检查接口和导入设置变化；更新对应适配器后重跑上述验证。真机记录必须补设备、操作系统、宿主版本、基础库、初始化结果、能力结果和导出产物审计。存在未知格子时不标“支持”。官方强制服务端变化不能仅靠回退本地 DLL 解决。

## 自动生成升级差异报告

```powershell
./tools/compare-sdk-inventory.ps1 -BeforePath before.json -AfterPath after.json -OutputPath upgrade-report.json
```

报告含前后版本、版本是否变化、文件新增/删除/修改、前后哈希及审查分类。即使版本号没变也比较内容；同版本被本地修改不能漏过。DLL 变化需要接口编译和运行验证，asmdef/meta 变化要复查平台裁剪及导入设置，jslib 变化要复查真机桥接。比较工具拒绝不同平台/包、重复路径、非法相对路径和无效哈希，不允许覆盖输入快照。

v2 快照覆盖包目录全部文件；v1 历史快照仅覆盖当时选取的文件。比较器拒绝混用 v1/v2，需要用同一导出器重新抓取升级前后两份包。报告不是 API 语义分析，也不判断升级是否兼容；结果固定 `compatibility=not-assessed`。版本号没变化或 changedFileCount=0 都不是免测条件。工具不会安装/降级 SDK 或修改工程。测试 `tests/run-inventory-tests.ps1` 已并入公共测试入口。

导出报告必须放在 SDK 包目录之外，以免报告本身参与下一次哈希。全文件哈希仍不证明来源完整性、外部依赖版本或服务端兼容性。

## 微信官方提交回归

来源：[官方仓库固定提交](https://github.com/wechat-miniprogram/minigame-tuanjie-transform-sdk/tree/915d4bf4f60ef0cd1c29316a8d67257185d609a7)。克隆原件保持干净，转换使用另一个完整副本，因为转换器会调整插件导入设置。0.1.1 与本地0.1.23是不同分发基线，不能按版本号推断兼容性。

运行 `tests/run-wechat-official-tests.ps1 -OfficialPackagePath <该官方包> -CompiledWxAssembly <同一包在Unity中编译的Wx.dll>`。脚本显式启用字符串上报分支并使用真实 WX 类型编译八套契约测试（包含支付网关）。登录失败 DTO 在此提交为 LoginFailCallbackErr，本地0.1.23为 RequestFailCallbackErr；生产适配使用类型推断，测试分别实例化实际 DTO。初始化200等返回码测试是注入行为，不能充当实际宿主初始化成功证据。

脚本版本检查只校验包标识，不能证明传入目录来源。执行前应核对固定 Git 提交与原件清洁状态，并确保 Wx.dll 来自同一工程；不可用另一个版本的缓存程序集混测。结果仅支持已列明范围，不代表支付、真机或所有官方 API 均受支持。

## 已验证的能力关闭构建组合

三平台分别在独立 Unity 工程关闭 Login/Storage/Share/Ads/Analytics，保留各自平台宏，执行实际 Development Build；全部成功退出 0。使用 check-linked-platform.ps1 的 ExcludedCapabilities 检查，各平台五类适配类型定义与引用均缺失，RuntimeInitializeOnLoadMethod 注册方法保留。微信仍保留字符串上报兼容宏，未以改宏绕过官方接口差异。

这覆盖“五类全部关闭”的组合和适配程序集，不覆盖所有单项组合或最终 JS/Wasm/资源的完整排除。后续微信、抖音、TikTok 七项全部关闭，以及 TikTok 仅开启登录/关闭其余六项的实际构建也已通过链接检查；后者保留登录、VerifiedAuth 与 UnitySessionExchange，网络/支付等六项自有实现缺失。仍未证明整个公共核心或官方单体 SDK 内部能力完全排除。默认开启模式的官方接口回归仍需在每次升级后重跑；两种验证不能互相替代。

三平台官方程序集仍保留关闭能力的方法，实际数量、官方 linker 规则及可重跑清单工具见 [官方 SDK 内部保留边界](../docs/platform-isolation.md#官方-sdk-内部保留边界2026-11-23-实测)。不能以适配类型缺失推断官方内部实现缺失。
