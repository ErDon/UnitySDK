# Android SDK 与 Unity 桥接工作计划

新增范围，独立于原小游戏计划的 72/84 计数。

- [x] 独立 Java 核心：七类能力契约、模块开关、错误、超时/取消、日志和释放。
- [x] Android AAR：持久化存储、系统文本分享、渠道工厂扩展与 JNI JSON 桥接。
- [x] Unity UPM：原生调用、多态工厂、主线程派发、配置和构建隔离。
- [x] 原生核心行为测试、真实 Android API 编译、AAR 结构和 Unity 程序集编译。
- [x] 原生/Unity 示例、接口说明、错误和接入文档。
- [ ] Android 真机及具体供应商验收（需要设备及供应商，不能以本地测试替代）。

原生实现采用 Java 8 兼容接口，Kotlin 可直接调用；避免要求接入游戏额外携带 Kotlin 运行库。Unity、Godot 和原生应用可复用 AAR，Godot 桥接不在本次实现中。

## 本轮验证记录（2026-11-24）

已完成 5/6 项；第 6 项为独立真机/供应商验收，不计入首版接口实现完成。

- Java 核心测试通过：模块关闭、所有权、取消/超时、重复/迟到回调、队列释放、错误与日志、网络请求头快照及注入拒绝。
- 真实 Android API 33 编译 AAR 与原生示例通过，最低 API 26，Java 8 字节码。AAR 不含第三方渠道；结构检查及同工具链重复构建哈希一致。
- Unity 2022.3.14f1c1 隔离工程输出 ANDROID_UNITY_CHECKS_PASS；验证配置、工厂释放、桥接 JSON、派发/关闭、支付确认边界和 AAR importer。日志：.tests/android-unity-final.log。
- Android Player 条件分支全开/全关两种 C# 编译通过；不代表 IL2CPP、JNI 真机或最终 APK 构建验收。
- 现有 tests/run-all-tests.ps1 全部通过；本轮未更改服务端实现，不重复运行服务端测试。
- 独立原生 SDK 支持 Java/Kotlin 调用；Unity 示例已在隔离工程编译；Godot 桥接未实现。
