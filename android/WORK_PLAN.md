# Android SDK 与 Unity 桥接工作计划

新增范围，独立于原小游戏计划的 72/84 计数。

- [x] 独立 Java 核心：七类能力契约、模块开关、错误、超时/取消、日志和释放。
- [x] Android AAR：持久化存储、系统文本分享、渠道工厂扩展与 JNI JSON 桥接。
- [x] Unity UPM：原生调用、多态工厂、主线程派发、配置和构建隔离。
- [x] 原生核心行为测试、真实 Android API 编译、AAR 结构和 Unity 程序集编译。
- [x] 原生/Unity 示例、接口说明、错误和接入文档。
- [x] 内置 HTTPS：请求/响应限额、超时、取消、队列上限及真实回环 TLS 验证。
- [x] ARM64 IL2CPP/R8 Development APK 构建及已知外平台 DEX/原生库检查。
- [x] Android 模拟器实际 JNI、存储、主线程回调、网络校验和冷启动持久化验证。
- [ ] Android 真机及具体供应商验收（需要设备及供应商，不能以本地测试替代）。

原生实现采用 Java 8 兼容接口，Kotlin 可直接调用；避免要求接入游戏额外携带 Kotlin 运行库。Unity、Godot 和原生应用可复用 AAR，Godot 桥接不在本次实现中。

## 本轮验证记录（2026-11-24）

首版记录：当时完成 5/6 项。后续新增三项已验证工作，当前为 **8/9 项完成**；剩余物理真机及具体供应商验收，不与模拟器结果混同。

- Java 核心测试通过：模块关闭、所有权、取消/超时、重复/迟到回调、队列释放、错误与日志、网络请求头快照及注入拒绝。
- 真实 Android API 33 编译 AAR 与原生示例通过，最低 API 26，Java 8 字节码。AAR 不含第三方渠道；结构检查及同工具链重复构建哈希一致。
- Unity 2022.3.14f1c1 隔离工程输出 ANDROID_UNITY_CHECKS_PASS；验证配置、工厂释放、桥接 JSON、派发/关闭、支付确认边界和 AAR importer。日志：.tests/android-unity-final.log。
- Android Player 条件分支全开/全关两种 C# 编译通过；不代表 IL2CPP、JNI 真机或最终 APK 构建验收。
- 现有 tests/run-all-tests.ps1 全部通过；本轮未更改服务端实现，不重复运行服务端测试。
- 独立原生 SDK 支持 Java/Kotlin 调用；Unity 示例已在隔离工程编译；Godot 桥接未实现。


## 通用网络与实际 Android 链路完善（2026-11-24）

新增默认 HttpsNetwork，原生开启 NETWORK 即可使用，保留自定义渠道覆盖。默认系统 TLS/主机名校验，拒绝明文、重定向跟随、非法请求头；2 工作线程/32 排队、64 KiB 请求体/1 MiB 响应，非 2xx 返回实际状态。新增 CancellableNetwork 可选接口，聚合取消/超时/关闭释放传输句柄，异步断连避免阻塞游戏线程。旧 Network 实现保持兼容。

Java 行为及真实本机 HTTPS 测试通过，覆盖证书拒绝、UTF-8 GET/POST、503、302 不跟随、分块超限、慢响应取消、满队列与关闭清理。测试只监听回环，不请求平台。

隔离工程使用 ARM64、IL2CPP、R8、min API 26、target API 33 构建开发 APK。在线模拟器（Cetus，支持 ARM64 转译）两次冷启动均输出 ANDROID_DEVICE_JNI_PASS，第二次输出 ANDROID_DEVICE_PERSISTENCE_PASS；包括原生 JSON/回调、中文与空值存储、删除、未配置渠道拒绝、默认网络拒绝明文和释放。未测试真实网络的 Android 证书环境、系统分享用户交互、物理真机或真实供应商。

重跑入口：tests/AndroidDevelopmentBuildChecks.cs + tests/AndroidDeviceSmoke.cs；tests/run-android-device-smoke.ps1 -Serial <设备>；tests/audit-android-apk.py。设备脚本只安装/重启 com.erdon.sdk.smoke，保留测试数据，不清理全局日志。APK 及 R8 检查记录见 validation/development-apk.json；只检查列明包名前缀/ABI，非穷举资源审计。原小游戏 72/84 计数不变。
