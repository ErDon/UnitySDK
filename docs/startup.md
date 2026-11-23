# 平台启动编排（开发中）

SdkStartup 串联 IPlatformRuntime 与 IPlatformSdkProviderFactory。构造时校验三个平台标识一致，并复制模块配置；Start 只调用官方初始化桥接。收到成功回调且 IsReady=true 后，下一次 Tick 才装配业务能力。失败、未就绪或超时不会替换当前 SDK；能力创建失败沿用 SDK 工厂回滚机制。

```csharp
using ErDon.UnitySDK;

// 在主线程创建；runtime 和 factory 必须来自同一个目标平台。
var startup = new SdkStartup(config, runtime, factory,
    postToMainThread, engineLog, result => {
        if (!result.Success)
            engineLog(SdkLogLevel.Error, result.Error.Code + " / " + result.Error.Reason + " callId=" + result.CallId);
        onReady(result);
    }, timeoutMilliseconds: 15000);
startup.Start();
// 将 startup 保留为宿主字段，在同一主线程 Update 中持续调用 startup.Tick()。
// 业务 SDK.Tick() 及 postToMainThread 的队列也必须由自定义宿主驱动。
// 取消本次等待：startup.Cancel();
// 宿主关闭：startup.Dispose(); SDK.Shutdown();
```

上述手动编排示例中的 config 为 SdkConfig，runtime 为 IPlatformRuntime，factory 为 IPlatformSdkProviderFactory；postToMainThread 为 Action<Action>，engineLog 为 Action<SdkLogLevel, string>，onReady 为 Action<StartupResult>，均由宿主提供且不能为空。onReady 需要检查 Success 再进入业务，失败时保持错误界面。示例中的 engineLog 调用是宿主显示结果，不改变 SDK 自己生成的结构化日志。Unity 游戏优先使用下方 UnitySdkHost.BeginStartup，宿主会自动 Tick，不要对同一尝试重复驱动。

| SdkStartup 接口 | 使用时机与效果 |
| --- | --- |
| 构造函数 | 校验身份/配置并取得 runtime 所有权；不会调用官方初始化 |
| Start() | 每个对象仅调用一次，开始官方初始化和超时计时 |
| Tick() | 创建线程每帧调用，消费完成结果或触发超时 |
| IsFinished | 表示本次尝试已终止，不等同于启动成功；成功与否读取完成回调 |
| Cancel() | 结束等待并回调 INIT.CANCELLED；不会关闭已运行业务 SDK |
| Dispose() | 静默结束等待并释放 runtime 桥接，不调用完成回调，也不释放已装配业务能力 |

构造成功后 startup 接管 runtime，在成功、失败、取消、超时或 Dispose 时只释放一次；这不会卸载或回滚官方宿主。构造抛异常时 runtime 仍归调用方。成功装配的业务能力归 SDK，不会被 startup.Dispose 释放。官方初始化未回调时默认 15 秒超时；在超时前接受的成功结果可在后续 Tick 交付，不要求每帧都触发 Tick。

Cancel 结束本地等待，包含已排队但尚未装配的成功回调，返回 INIT.CANCELLED。Dispose 静默放弃尝试、不调用完成回调；平台迟到结果不再装配。Start/Tick/Cancel/Dispose 必须在创建线程调用，官方回调可以来自后台线程。仅首个官方结果生效，返回成功但 IsReady=false 为 INIT.NOT_READY；工厂异常映射 INIT.COMPOSITION_FAILED，原异常正文不输出。

StartupResult 包含 CallId/TraceId，错误关联 Operation=SDK.Startup、Stage=Initialization。日志由 SDK 生成时间、接口、平台、timeoutMilliseconds、CallId/logId/traceId 和结果；上层日志回调异常时回退 engineLog。该编排不证明平台账号授权、导出成功或真机可用，也不会自动重试。

## Unity 自动驱动入口

```csharp
using ErDon.UnitySDK;

// runtime 和 factory 由目标平台接入层创建，每次启动使用新的实例。
UnitySdkHost.BeginStartup(config, runtime, factory,
    result => {
        if (!result.Success) {
            UnityEngine.Debug.LogError(result.Error.Code + " / " + result.Error.Reason + " callId=" + result.CallId);
            return;
        }
        // 现在允许游戏调用已勾选能力；启动成功不表示玩家已登录。
    }, timeoutMilliseconds: 15000);
```

例如抖音使用 DouyinRuntime 和 DouyinProviderFactory；构造参数和 HTTP 会话桥接见 [抖音文档](douyin.md)。微信和 TikTok 必须使用各自的 runtime/factory，不能混用平台身份。也可使用 [配置启动入口](configuration.md) 自动选择已注册的平台。config.LogCallback 未提供时使用 Unity 日志输出。UnitySdkHost.Update 自动驱动启动超时、SDK.Tick 和主线程队列。

- BeginStartup 替换并静默释放旧启动尝试，不调用旧完成回调；传入无效构造参数时保留旧任务。每次提供新的 runtime 桥接实例。
- CancelStartup 返回 INIT.CANCELLED 并结束当前等待，已运行的业务 SDK 不会被关闭；不撤销官方 SDK 已进行的动作。
- UnitySdkHost.Shutdown 同时放弃启动任务和释放业务 SDK。使用宿主启动流程时应调用该入口；直接 SDK.Shutdown 不负责关闭宿主持有的启动任务。
- Initialize / InitializeWithFactory 成功装配后才放弃待处理启动；配置、工厂或实例校验失败时保留旧启动与现有 SDK。直接装配依然要求官方宿主已就绪。
- 宿主销毁时静默释放启动并 Shutdown。所有这些入口在 Unity 主线程调用，不允许从 SDK 日志回调重入。

验证：tests/StartupPlayChecks.cs.txt 在隔离 Unity 2022.3 工程运行 Play Mode，覆盖自动启动后登录、自动超时、任务替换、取消、Shutdown 及迟到回调抑制。使用模拟 runtime/factory，只验证宿主编排。三平台启动适配和注册已实现，真实官方初始化结果仍需真机验收，不能从模拟测试推导成功。

## Editor 无域重载

UnitySdkHost 在 SubsystemRegistration 阶段清理上一会话的 SDK 和等待启动任务。因此关闭 Reload Domain / Reload Scene 后，每次进入 Play Mode 仍需按当前配置重新初始化；编辑态创建的 provider 不会继续带入游戏。退出 Play Mode 通过宿主销毁释放已拥有的 provider，旧回调失效。

宿主队列异常由 SDK 输出 `interface=UnitySdkHost.Update`、`stage=Callback`、`result=CALLBACK.EXCEPTION`，带时间、参数及独立诊断 ID；该 ID 标识宿主异常事件，不冒充未知的业务调用 ID。原始异常消息不会输出。仍使用已配置的上层日志回调，回调缺省或抛出时回退到 Unity。

`tests/ReloadPlayChecks.cs.txt` 可放入隔离 Unity 工程 Assets/Editor，以 `-executeMethod ReloadPlayChecks.Run` 运行（不传 `-quit`，测试自行退出），成功标记 `SDK_RELOAD_PLAY_PASS`。测试会暂时关闭域/场景重载，正常结束恢复原选项；应只用于隔离测试工程。
