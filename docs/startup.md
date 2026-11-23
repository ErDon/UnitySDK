# 平台启动编排（开发中）

SdkStartup 串联 IPlatformRuntime 与 IPlatformSdkProviderFactory。构造时校验三个平台标识一致，并复制模块配置；Start 只调用官方初始化桥接。收到成功回调且 IsReady=true 后，下一次 Tick 才装配业务能力。失败、未就绪或超时不会替换当前 SDK；能力创建失败沿用 SDK 工厂回滚机制。

```csharp
// 在主线程创建；runtime 为 DouyinRuntime，factory 为 DouyinProviderFactory。
startup = new SdkStartup(config, runtime, factory,
    postToMainThread, engineLog, result => {
        if (!result.Success) ShowStartupError(result.Error.Code, result.CallId);
        else EnterGame();
    }, timeoutMilliseconds: 15000);
startup.Start();
// 在同一主线程 Update 中持续调用 startup.Tick()；业务 SDK.Tick 也仍需执行。
```

示例中的 postToMainThread、engineLog、ShowStartupError、EnterGame 由宿主提供。UnitySdkHost 的便捷启动入口仍待接入，当前不要误以为它已经自动 Tick 这次启动。一个宿主应只保留一个进行中的启动尝试。

构造成功后 startup 接管 runtime，在成功、失败、取消、超时或 Dispose 时只释放一次；这不会卸载或回滚官方宿主。构造抛异常时 runtime 仍归调用方。成功装配的业务能力归 SDK，不会被 startup.Dispose 释放。官方初始化未回调时默认 15 秒超时；在超时前接受的成功结果可在后续 Tick 交付，不要求每帧都触发 Tick。

Cancel 结束本地等待，包含已排队但尚未装配的成功回调，返回 INIT.CANCELLED。Dispose 静默放弃尝试、不调用完成回调；平台迟到结果不再装配。Start/Tick/Cancel/Dispose 必须在创建线程调用，官方回调可以来自后台线程。仅首个官方结果生效，返回成功但 IsReady=false 为 INIT.NOT_READY；工厂异常映射 INIT.COMPOSITION_FAILED，原异常正文不输出。

StartupResult 包含 CallId/TraceId，错误关联 Operation=SDK.Startup、Stage=Initialization。日志由 SDK 生成时间、接口、平台、timeoutMilliseconds、CallId/logId/traceId 和结果；上层日志回调异常时回退 engineLog。该编排不证明平台账号授权、导出成功或真机可用，也不会自动重试。
