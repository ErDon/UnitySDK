# 第一阶段接入与验证

当前版本是核心骨架，不含任何真实平台 SDK。Unity 基线 2022.3；首次可用发布前仍需 Unity 导入及真机验证。

通过 Unity Package Manager 的 Add package from disk 选择 `Packages/com.erdon.unitysdk/package.json`。导入 Mock Login 示例，将 MockLoginExample 挂到场景对象，在 Editor Play 查看结果；示例仅在 Editor 编译，不是真实账号。

初始化暂由 `UnitySdkHost.Initialize(config, provider)` 显式注入 IAuth。后续配置面板生成平台注册，不要求游戏自己选择实现。Initialize / Login / Shutdown 目前要求在 Unity 主线程调用；异步平台完成通过主线程队列派发。Shutdown 后旧操作回调被丢弃；等待调用方需同步结束自己的加载状态。

`SDK.Login(callback)`：无参数；返回 LoginResult。成功 Session 为会话凭据，禁止输出。可能错误：SDK.NOT_INITIALIZED、MODULE.DISABLED、CAPABILITY.NOT_SUPPORTED、AUTH.IN_PROGRESS、AUTH.UNKNOWN，以及适配器定义的已映射错误。当前未实现自动超时，后续需要超时及取消契约。

`SdkConfig.LogCallback = (level, message) => GameLogger.Write(level, message)`：正文由 SDK 提供；不设置则 Unity Debug 输出。回调异常回退默认日志。当前登录无入参，params 为 {}；凭据及任意平台原始异常不写日志。正式诊断字段和更多能力待后续迭代。

验证：tests/CoreTests.cs 是无外部依赖的核心行为测试，与 SDK.cs 一起编译执行。覆盖重复和过期回调、凭据不写日志、日志字段、日志回调异常、模块关闭及未初始化。不代表 Unity 导入或真实平台验证。
