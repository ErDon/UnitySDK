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

上述手动编排示例中的 postToMainThread、engineLog、ShowStartupError、EnterGame 由宿主提供。Unity 游戏优先使用下方 UnitySdkHost.BeginStartup，宿主会自动 Tick，不要对同一尝试重复驱动。

构造成功后 startup 接管 runtime，在成功、失败、取消、超时或 Dispose 时只释放一次；这不会卸载或回滚官方宿主。构造抛异常时 runtime 仍归调用方。成功装配的业务能力归 SDK，不会被 startup.Dispose 释放。官方初始化未回调时默认 15 秒超时；在超时前接受的成功结果可在后续 Tick 交付，不要求每帧都触发 Tick。

Cancel 结束本地等待，包含已排队但尚未装配的成功回调，返回 INIT.CANCELLED。Dispose 静默放弃尝试、不调用完成回调；平台迟到结果不再装配。Start/Tick/Cancel/Dispose 必须在创建线程调用，官方回调可以来自后台线程。仅首个官方结果生效，返回成功但 IsReady=false 为 INIT.NOT_READY；工厂异常映射 INIT.COMPOSITION_FAILED，原异常正文不输出。

StartupResult 包含 CallId/TraceId，错误关联 Operation=SDK.Startup、Stage=Initialization。日志由 SDK 生成时间、接口、平台、timeoutMilliseconds、CallId/logId/traceId 和结果；上层日志回调异常时回退 engineLog。该编排不证明平台账号授权、导出成功或真机可用，也不会自动重试。

## Unity 自动驱动入口

```csharp
UnitySdkHost.BeginStartup(config, new ErDon.UnitySDK.Douyin.DouyinRuntime(), factory,
    result => {
        if (!result.Success) { UnityEngine.Debug.LogError(result.Error.Code + " " + result.CallId); return; }
        SDK.Login(login => { /* 游戏登录逻辑 */ });
    }, timeoutMilliseconds: 15000);
```

factory 使用 DouyinProviderFactory；构造函数参数和 HTTP 会话桥接见 [抖音文档](douyin.md)。此例仅表示已实现的抖音初始化桥接，不能将它用于微信或 TikTok。config.LogCallback 未提供时使用 Unity 日志输出。UnitySdkHost.Update 自动驱动启动超时、SDK.Tick 和主线程队列。

- BeginStartup 替换并静默释放旧启动尝试，不调用旧完成回调；传入无效构造参数时保留旧任务。每次提供新的 runtime 桥接实例。
- CancelStartup 返回 INIT.CANCELLED 并结束当前等待，已运行的业务 SDK 不会被关闭；不撤销官方 SDK 已进行的动作。
- UnitySdkHost.Shutdown 同时放弃启动任务和释放业务 SDK。使用宿主启动流程时应调用该入口；直接 SDK.Shutdown 不负责关闭宿主持有的启动任务。
- 旧的 Initialize / InitializeWithFactory 会先放弃待处理启动，再执行直接装配；依然要求官方宿主已就绪。
- 宿主销毁时静默释放启动并 Shutdown。所有这些入口在 Unity 主线程调用，不允许从 SDK 日志回调重入。

验证：tests/StartupPlayChecks.cs.txt 在隔离 Unity 2022.3 工程运行 Play Mode，覆盖自动启动后登录、自动超时、任务替换、取消、Shutdown 及迟到回调抑制。使用模拟 runtime/factory，不代表真机初始化或微信/TikTok 启动适配已经完成。

## Editor 无域重载

UnitySdkHost 在 SubsystemRegistration 阶段清理上一会话的 SDK 和等待启动任务。因此关闭 Reload Domain / Reload Scene 后，每次进入 Play Mode 仍需按当前配置重新初始化；编辑态创建的 provider 不会继续带入游戏。退出 Play Mode 通过宿主销毁释放已拥有的 provider，旧回调失效。

宿主队列异常由 SDK 输出 `interface=UnitySdkHost.Update`、`stage=Callback`、`result=CALLBACK.EXCEPTION`，带时间、参数及独立诊断 ID；该 ID 标识宿主异常事件，不冒充未知的业务调用 ID。原始异常消息不会输出。仍使用已配置的上层日志回调，回调缺省或抛出时回退到 Unity。

`tests/ReloadPlayChecks.cs.txt` 可放入隔离 Unity 工程 Assets/Editor，以 `-executeMethod ReloadPlayChecks.Run` 运行（不传 `-quit`，测试自行退出），成功标记 `SDK_RELOAD_PLAY_PASS`。测试会暂时关闭域/场景重载，正常结束恢复原选项；应只用于隔离测试工程。
