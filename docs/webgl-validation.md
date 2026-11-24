# WebGL 单线程与独立工程验证

小游戏运行时使用 Unity 主循环处理超时、取消和回调。异步接口不等于后台线程；不要在公共层或小游戏适配层加入 `Task.Run`、线程池、阻塞等待或线程计时器。Android 原生 Java 的线程实现限定在 Android AAR 中。

`Interlocked`、`ConcurrentQueue` 和读取当前线程 ID 不会自行创建线程，保留用于回调去重与线程归属检查。网络等待使用现有 UnityWebRequest/协程实现，不要用 `.Wait()` 或 `.Result` 同步等待。

## 构建约束

WebGL 构建开启 `PlayerSettings.WebGL.threadsSupport` 时，SDK 构建检查返回 `WEBGL.THREADS_UNSUPPORTED`，请关闭该选项。[Unity 官方说明](https://docs.unity3d.com/cn/6000.0/ScriptReference/PlayerSettings.WebGL-threadsSupport.html) 区分了原生 C/C++ WebAssembly 线程与 C# 线程；本库采用不依赖 worker 的小游戏基线。当前实际验证版本为 Unity 2022.3.14f1c1。

编译后检查实际 Player 程序集，而不是只搜索源码：

```powershell
./tests/run-webgl-thread-tests.ps1
./tools/check-webgl-threads.ps1 -ManagedDirectory '<工程>/Library/Bee/artifacts/2000b0aPDevDbg.dag'
./tools/check-webgl-threads.ps1 -ManagedDirectory '<工程>/Library/Bee/artifacts/WebGL/ManagedStripped'
```

中间目录名由 Unity 生成，升级后应选择实际 Player 编译输出。检查器读取 IL 调用，涵盖线程、线程池、Task 调度/阻塞、计时器和常见同步等待；允许队列与原子操作。Android 运行时程序集混入 WebGL 或没有任何自有运行时程序集时会失败。

检查范围是 `ErDon.UnitySDK*` 的直接调用，包括编译器生成的方法；不解析反射、第三方间接调用、原生插件或 JavaScript，不是线程安全证明。不要将所有 `await` 都当作线程：编译器生成的完成结果读取也可能完全在主线程执行。新增第三方依赖仍需单独审查。

## 第二个独立工程

```powershell
./tests/prepare-second-game.ps1 -WeChatPackage '<官方微信 UPM 目录>'
```

该脚本创建全新的 `.tests/SecondGameWebGLProject`，拒绝覆盖已有工程；仅引用公共 SDK、HTTP、微信及 Android 包。用 Unity 2022.3.14f1c1 打开工程，或以 `-batchmode -quit -buildTarget WebGL -executeMethod SecondGameBuildChecks.Build` 运行。若第一次改变宏触发域重载导致命令中断，等待退出后再次运行同一命令。

探针使用 `independent-second-game`，不调用真实登录、广告、支付或上报；验证开启线程会被拒绝，再关闭线程构建。当前固定的官方微信包需要 `ERDON_WECHAT_STRING_ANALYTICS`，测试入口已配置该宏。其他官方版本应按 ReportEvent 的真实签名选择兼容宏，不能直接套用。

```powershell
./tools/check-linked-platform.ps1 -ManagedDirectory '.tests/SecondGameWebGLProject/Library/Bee/artifacts/WebGL/ManagedStripped' -Platform WeChat
```

## 本轮进度（2026-11-24）

- [x] 构建线程开关拦截，真实 Unity 调用输出 `SDK_WEBGL_THREAD_GUARD_PASS`。
- [x] IL 检查器正反例及公共回归通过；三平台现有 Player 输出无列明的禁止调用。
- [x] 第二个独立微信工程开发构建成功，包含 Android UPM，但最终托管输出不含 Android/抖音/TikTok 适配及其引用。
- [x] 同一注入存储后端下，两款游戏及测试/生产环境的同名键隔离、重新创建实例及删除隔离通过。
- [ ] 第二个游戏在微信宿主内启动、接口调用和真实持久化验收。
- [ ] 官方转换后的最终 JS/Wasm/资源完整归属与平台隔离验收。

产物字面标识检查记录在 [审计记录](../compatibility/second-game-webgl-markers-2026-11-24.json)，包含四个产物的 SHA-256。未出现所列 Android/抖音/TikTok 标识只是有限证据，不能证明混淆代码或合并资源全部排除。当前产物是 Unity 原始 WebGL 开发包，未将它当作微信转换产物或真机通过。

这些补充验证不改变主计划 72/84 的大项计数；跨游戏完整验收与最终隔离仍待完成。
