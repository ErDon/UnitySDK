# 核心接入（开发版）

Unity 基线 2022.3。通过 Package Manager 的 Add package from disk 选择 Packages/com.erdon.unitysdk/package.json。微信、抖音、TikTok 适配位于独立平台包。完整产物与真机验收尚未完成，正式 Player 构建仍被阻断。非生产配置可使用 Development Build 进行技术验证，但必须通过目标适配器、WebGL 平台宏及混装检查；不支持将 Mock 打入 Player。

## 配置驱动的 Mock

打开 Tools → ErDon SDK → 配置与预览，填写 Game ID，选择 Mock，勾选登录。进入 Play Mode，选择模拟结果，点击初始化和登录按钮。关闭登录后重新初始化，应返回 MODULE.DISABLED。此流程验证 Editor Mock；真实平台需安装对应适配包和官方插件，并按启动文档初始化。

## 游戏调用

需要完整可导入的真实平台启动脚本时，安装 `com.erdon.unitysdk.transport.http`，在 Package Manager 的 Samples 导入 **Configured Platform Startup**。它读取面板导出的 JSON，按勾选创建会话兑换和网络能力，等待初始化后由游戏按钮触发登录。操作与生命周期说明见 [示例 README](../Packages/com.erdon.unitysdk.transport.http/Samples~/ConfiguredStartup/README.md)。该示例已通过编译和独立 Unity Play Mode 的组件生命周期检查（平台注册替身）；真实平台运行与服务端联调仍需完成。

真实平台优先使用 [配置启动流程](startup.md)，平台包已提供注册和按需工厂。手动场景也可由 UnitySdkHost.Initialize(config, provider) 传入 IAuth。游戏调用 SDK.Login(callback)，结果含 Success、Session、Error、CallId、TraceId。禁止打印 Session。

默认超时 15000ms；Login(callback, timeoutMilliseconds) 接受正数超时。CancelLogin 只取消本地等待，不保证撤销平台动作。Shutdown 丢弃旧调用回调，游戏需同时结束自己的等待 UI。UnitySdkHost.Update 驱动 Tick；暂停期间不派发，恢复后检查超时。初始化、调用、取消、释放需在主线程进行。

## 错误与日志

错误包含 Code、Reason、Operation、Stage、CallId、PlatformCode、PlatformMessage。适配器仅提交脱敏原始信息；未知失败不推断地区或封禁。Retry 默认 Unknown；WithRetry 可设置明确策略与等待秒数，禁止猜测限流恢复时间。客户端 TraceId 等于 CallId；后端交换与订单协议另有请求关联字段，通用网络请求不会自动注入 HTTP 追踪头。

LogCallback 接收级别和 SDK 格式化的完整字符串；不提供则 Unity Debug 输出。日志包含时间、接口、参数、调用/日志 ID、平台、阶段和结果。当前登录仅记录 timeoutMilliseconds；Session 和任意平台异常正文不自动记录。回调只能输出，不得重入 SDK 操作；异常回退默认日志。业务结果回调异常会被隔离，记录 CALLBACK.EXCEPTION。

## 测试

运行 tests/run-core-tests.ps1 验证核心行为。tests/SdkEditorChecks.cs.txt 和 tests/SdkPlayChecks.cs.txt 复制到独立测试工程 Assets/Editor，改为 .cs 后分别执行静态 Run 方法；Play 检查进入真实 Play Mode 验证配置、成功/失败与 Update 超时。测试会修改测试工程设置并退出 Unity，不要在业务工程运行。

上述测试不代替真机、支付或实际发布验证。

配置持久化检查使用 `tests/SettingsPersistenceChecks.cs.txt`：复制为独立工程的 `Assets/Editor/SettingsPersistenceChecks.cs`，依次在三个单独的 Unity 进程中以 `-batchmode -nographics -quit -projectPath <隔离工程> -executeMethod <方法>` 执行 `SettingsPersistenceChecks.SaveA`、`SettingsPersistenceChecks.VerifyAAndSaveB`、`SettingsPersistenceChecks.VerifyB`。每轮正常退出后再启动下一轮。成功标记依次为 `SDK_SETTINGS_SAVE_A_PASS`、`SDK_SETTINGS_RELOAD_A_SAVE_B_PASS`、`SDK_SETTINGS_RELOAD_B_PASS`。它会覆盖该测试工程的 SDK 配置，验证两个游戏/平台标识、环境和七个能力开关在重启后的恢复与 JSON 映射；不会启动官方平台。不要在业务工程执行。

## 初始化与释放边界

SDK 拥有传入 IAuth 的生命周期，重新初始化或 Shutdown 会释放旧 provider。重新初始化必须创建新的 provider；复用仍在使用的同一个实例会抛 ArgumentException，旧实例保持有效。已释放实例不得再次传入。

初始化和释放期间，provider 不得重入 SDK 操作；这样的调用抛 InvalidOperationException，避免旧 provider 修改新实例状态。普通登录结果回调可以重新初始化并开始新的登录。日志回调仍只能输出，不能重入 SDK。

Shutdown 使旧请求及已入队的旧结果失效，不再派发结果。重复 Shutdown 不会重复释放 provider。Dispose 抛异常时，SDK 继续清理并输出格式化 SDK.DISPOSE_FAILED，不输出异常原文。新的初始化仍可成功。任何 provider 返回空游戏 session 都会转换为 AUTH.INVALID_SESSION。

上述边界由 tests/run-all-tests.ps1 中的生命周期测试覆盖。SDK 公共操作仍要求主线程调用，不承诺并发 Initialize/Shutdown。


## 按能力接入

[上层接口索引与生命周期](api-reference.md) 汇总全部 SDK 聚合入口、宿主入口、取消方式和日志回调示例。

- [登录与服务端会话](authentication.md)、[平台启动与配置](startup.md)
- [存储](storage.md)、[分享](share.md)、[激励广告](rewarded-ads.md)
- [网络请求](network.md)、[数据上报](analytics.md)
- [订单](orders.md)、[支付编排](payment-pipeline.md)
- [平台隔离](platform-isolation.md)、[Android/iOS 扩展](native-extension.md)

所有接口以各能力文档的当前实现说明为准；整体架构草案包含尚未完成的发布目标。工作计划顶部勾选项列明已验证范围，未勾选的真机、真实支付与最终裁剪不视为完成。


## 复核能力文档示例

分享、存储、广告、上报、登录、网络与支付编排文档中的 C# 片段是游戏方法内的调用示例；`using` 放在源文件顶部，调用语句放进自己的方法。支付示例中命名的订单客户端、平台收银台、主线程派发器和会话由游戏接入层提供，并非默认可用的真实支付实现。

导入核心及 HTTP 包、等待 Unity 编译完成后，可运行：

```powershell
./tests/compile-capability-doc-examples.ps1 -CoreAssembly '你的工程/Library/ScriptAssemblies/ErDon.UnitySDK.dll' -HttpAssembly '你的工程/Library/ScriptAssemblies/ErDon.UnitySDK.Transport.Http.dll'
```

默认 Unity 编译器目录为 2022.3.14f1c1，可通过 `-UnityData` 指定其他安装的 Editor/Data 路径。脚本在仓库 `.tests/documentation-examples` 生成临时包装类和 DLL，逐段编译上述七份能力文档、接口索引、配置、启动及原生扩展文档的全部 C# 代码块（当前 21 段），失败立即停止；不会运行这些调用或访问平台。完整工厂类型直接编译，调用片段包入 MonoBehaviour 方法；配置资源作为 TextAsset 参数传入，底层网络示例补入文中要求的 provider 字段及 OnDestroy 释放方法。它验证文档与所传程序集的接口兼容性，不证明程序集是最新构建，也不代替真机、后端与支付验收。

平台接入文档另有微信 4 段、抖音 3 段、TikTok 3 段。安装目标官方依赖并让独立 Unity 工程编译完成后，可分别增加 `-WeChatAssembly`、`-DouyinAssembly` 或 `-TikTokAssembly` 参数，指向对应的 `Library/ScriptAssemblies/ErDon.UnitySDK.<平台>.dll`；同时将 CoreAssembly 和 HttpAssembly 指向同一工程。每个平台单独执行，不要求把多个官方 SDK 混装到同一个工程。省略平台参数只检查公共 21 段，不表示平台文档已通过；提供不存在的程序集路径会失败。成员方法直接放入包装组件，不通过补写空方法来掩盖缺失依赖。三平台合计新增 10 段已分别通过编译，全部独立示例共 31 段。
