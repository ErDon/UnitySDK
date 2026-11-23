# 核心接入（开发版）

Unity 基线 2022.3。通过 Package Manager 的 Add package from disk 选择 Packages/com.erdon.unitysdk/package.json。微信、抖音、TikTok 适配位于独立平台包。完整产物与真机验收尚未完成，Player 构建仍会被主动阻断。

## 配置驱动的 Mock

打开 Tools → ErDon SDK → 配置与预览，填写 Game ID，选择 Mock，勾选登录。进入 Play Mode，选择模拟结果，点击初始化和登录按钮。关闭登录后重新初始化，应返回 MODULE.DISABLED。此流程验证 Editor Mock；真实平台需安装对应适配包和官方插件，并按启动文档初始化。

## 游戏调用

真实平台优先使用 [配置启动流程](startup.md)，平台包已提供注册和按需工厂。手动场景也可由 UnitySdkHost.Initialize(config, provider) 传入 IAuth。游戏调用 SDK.Login(callback)，结果含 Success、Session、Error、CallId、TraceId。禁止打印 Session。

默认超时 15000ms；Login(callback, timeoutMilliseconds) 接受正数超时。CancelLogin 只取消本地等待，不保证撤销平台动作。Shutdown 丢弃旧调用回调，游戏需同时结束自己的等待 UI。UnitySdkHost.Update 驱动 Tick；暂停期间不派发，恢复后检查超时。初始化、调用、取消、释放需在主线程进行。

## 错误与日志

错误包含 Code、Reason、Operation、Stage、CallId、PlatformCode、PlatformMessage。适配器仅提交脱敏原始信息；未知失败不推断地区或封禁。Retry 默认 Unknown；WithRetry 可设置明确策略与等待秒数，禁止猜测限流恢复时间。客户端 TraceId 等于 CallId；后端交换与订单协议另有请求关联字段，通用网络请求不会自动注入 HTTP 追踪头。

LogCallback 接收级别和 SDK 格式化的完整字符串；不提供则 Unity Debug 输出。日志包含时间、接口、参数、调用/日志 ID、平台、阶段和结果。当前登录仅记录 timeoutMilliseconds；Session 和任意平台异常正文不自动记录。回调只能输出，不得重入 SDK 操作；异常回退默认日志。业务结果回调异常会被隔离，记录 CALLBACK.EXCEPTION。

## 测试

运行 tests/run-core-tests.ps1 验证核心行为。tests/SdkEditorChecks.cs.txt 和 tests/SdkPlayChecks.cs.txt 复制到独立测试工程 Assets/Editor，改为 .cs 后分别执行静态 Run 方法；Play 检查进入真实 Play Mode 验证配置、成功/失败与 Update 超时。测试会修改测试工程设置并退出 Unity，不要在业务工程运行。

上述测试不代替真机、支付或实际发布验证。

## 初始化与释放边界

SDK 拥有传入 IAuth 的生命周期，重新初始化或 Shutdown 会释放旧 provider。重新初始化必须创建新的 provider；复用仍在使用的同一个实例会抛 ArgumentException，旧实例保持有效。已释放实例不得再次传入。

初始化和释放期间，provider 不得重入 SDK 操作；这样的调用抛 InvalidOperationException，避免旧 provider 修改新实例状态。普通登录结果回调可以重新初始化并开始新的登录。日志回调仍只能输出，不能重入 SDK。

Shutdown 使旧请求及已入队的旧结果失效，不再派发结果。重复 Shutdown 不会重复释放 provider。Dispose 抛异常时，SDK 继续清理并输出格式化 SDK.DISPOSE_FAILED，不输出异常原文。新的初始化仍可成功。任何 provider 返回空游戏 session 都会转换为 AUTH.INVALID_SESSION。

上述边界由 tests/run-all-tests.ps1 中的生命周期测试覆盖。SDK 公共操作仍要求主线程调用，不承诺并发 Initialize/Shutdown。


## 按能力接入

- [登录与服务端会话](authentication.md)、[平台启动与配置](startup.md)
- [存储](storage.md)、[分享](share.md)、[激励广告](rewarded-ads.md)
- [网络请求](network.md)、[数据上报](analytics.md)
- [订单](orders.md)、[支付编排](payment-pipeline.md)
- [平台隔离](platform-isolation.md)、[Android/iOS 扩展](native-extension.md)

所有接口以各能力文档的当前实现说明为准；整体架构草案包含尚未完成的发布目标。工作计划顶部勾选项列明已验证范围，未勾选的真机、真实支付与最终裁剪不视为完成。
