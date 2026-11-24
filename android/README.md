# ErDon Android SDK（0.1.0 开发预览）

独立 Java/Android SDK 与 Unity 桥接共用一套原生实现。Java 8 字节码、Android API 26+，不包含任何第三方登录/广告/支付渠道。能力、错误及 Unity 接入详见 [Android 指南](../docs/android.md)。

## 原生应用安装

将构建得到的 `build/erdon-android-0.1.0.aar` 放入应用 `libs`。也可直接使用仓库 [UPM 内置的相同 AAR](../Packages/com.erdon.unitysdk.android/Runtime/Plugins/Android/erdon-android.aar)。在应用模块 Gradle 配置 `implementation(files("libs/erdon-android-0.1.0.aar"))`，minSdk 至少 26，Java source/targetCompatibility 为 1.8。目标 SDK 由宿主应用决定。

不需要 UnityPlayer、UnitySendMessage、Kotlin 运行库或额外 Maven 下载。consumer-rules.pro 随 AAR 发布，保护 JNI 名称。网络权限只在宿主实际使用网络能力时自行声明；SDK 不自动申请隐私权限、不自动收集设备标识。

## 原生上层示例

```java
import com.erdon.sdk.NativeSdk;
import com.erdon.sdk.android.AndroidSdk;
import java.util.EnumSet;

// 在 Activity 中创建；真实渠道未接入前只开存储和文本分享。
NativeSdk sdk = AndroidSdk.create(this, "my-game", "test",
    EnumSet.of(NativeSdk.Module.STORAGE, NativeSdk.Module.SHARE),
    null, line -> android.util.Log.i("MyGameSDK", line));

NativeSdk.Result<String> saved = sdk.setStorage("language", "zh");
NativeSdk.Result<String> loaded = sdk.getStorage("language");
sdk.share("我的游戏", "https://example.com/game", result -> {
    if (!result.success()) android.util.Log.w("MyGameSDK", result.code);
});
// Activity 销毁时 sdk.close()；再次创建 Activity 必须创建新的 SDK。
```

省略日志回调（传 null）时使用 Logcat。日志由 SDK 格式化，包含接口、时间、脱敏参数、callId、traceId、唯一 logId；回调抛异常时用相同内容兜底。不要在日志回调里重入 SDK。

## 每项接口调用

以下 `sdk` 是上面创建的实例。未启用模块返回 MODULE.DISABLED；开启但无 provider 返回 CAPABILITY.NOT_SUPPORTED，不会伪造成功。所有异步方法返回 Operation；cancel 仅停止本地等待，不能取消远程订单。

```java
// 1. 登录：渠道应先与游戏后端兑换；value 为已验证游戏会话，不是渠道 token/openId。
NativeSdk.Operation login = sdk.login(result -> {
    if (!result.success()) return; // 根据 code、reason、platformCode、callId 展示及诊断
    String gameSession = result.value; // 禁止写入日志
});
// 2. 存储：get 不存在返回 STORAGE.NOT_FOUND；空字符串可正常保存。
sdk.setStorage("key", "");
sdk.getStorage("key");
sdk.removeStorage("key");
// 3. 文本分享：成功表示启动分享面板，不表示用户真正发送。
sdk.share("标题", "正文或完整链接", result -> { });
// 4. 广告：value=false 为提前关闭；客户端结果不等于服务端奖励凭证。
sdk.showAd("your-unit-id", result -> { });
// 5. 支付：仅接受游戏后端准备的渠道参数，返回后仍需服务端查单和幂等发货。
sdk.launchPayment("server-order-id", "{\"channelPayload\":\"server-signed-data\"}", result -> { });
// 6. 网络：HTTPS GET/POST 扩展接口；当前没有内置网络供应商。
// 如需请求头，使用 sdk.send(new NativeSdk.HttpRequest(url, method, body, headers), callback)。
sdk.send("https://api.example.com/status", "GET", "", result -> {
    if (result.success()) { int httpStatus = result.value.status; }
});
// 7. 上报：参数 JSON 由渠道校验，成功只代表渠道接受调用。
sdk.report("level_complete", "{\"level\":1}", result -> { });
login.cancel();
```

推荐将每项渠道实现拆成独立 Android library/AAR。实现对应接口后，在 `AndroidSdk.create` 的 channels 参数中按 Module 创建新对象；未匹配返回 null（存储/分享会回落到内置实现）。不要返回同一个已被旧 SDK 释放的实例。Factory 是构造器，不在创建 SDK 时启动所有渠道。

Unity 宿主在应用启动时调用 `UnityBridge.installChannels(AndroidSdk.ChannelFactory)` 安装同一工厂，再启动 C# SDK。它只影响之后创建的实例。初始化渠道所需的 Activity 通过工厂参数传入，公开 App ID、环境和后端地址由宿主配置；密钥留在后端。第三方 SDK 的初始化、Activity 回调或 consent 流程由其独立渠道包负责，本包不猜测供应商 API。

## 渠道实现约定

| 接口 | 完成结果 | 实现方责任 |
| --- | --- | --- |
| Login | `Result.ok(gameSession)` | 平台凭证交后端验证，不返回空会话；安全映射错误 |
| Storage | `Result<String>` | 可靠持久化、区分不存在，close 不清除数据 |
| Share | `Result<Void>` | 发起系统/渠道分享，不能承诺接收者收到 |
| Ads | `Result<Boolean>` | 完整观看返回 true；提前关闭 false；错误用 fail |
| Payments | `Result<Void>` | 消费已准备参数，绝不能发货或信任客户端金额 |
| Network | `Result<HttpResponse>` | 超时、响应限额、安全 HTTPS，禁止携带凭据跟随重定向；非 2xx 也保留 HTTP 状态 |
| Analytics | `Result<Void>` | 校验事件/参数，不上传未经授权的个人信息 |

provider 回调可来自其他线程，核心统一派发到配置 executor；AndroidSdk 默认派发 Android UI 线程。provider 方法须快速返回；超时不可能解救一个阻塞 UI 线程的供应商。每个返回的 Operation 只负责本地观察，close 时还须解除渠道监听。core 不拥有 Activity 或供应商全局单例。

需要自定义超时/调度/日志兜底时直接构造 `NativeSdk`，设置 factory 和 executor；不依赖 Android 的核心源码位于 `core/src`。默认 AndroidSdk 超时为 15 秒，可通过 create 的 timeoutMs 重载设置；不要未经调整就等待长时间广告/收银台：超时后保留原订单，由游戏后端恢复；不要换幂等键重下单。

## 离线构建与验证

需要 JDK 17/21 和已安装的 Android SDK。这里不自动下载 Gradle、Android SDK 或供应商包。

```powershell
python android/test.py
python android/build.py --android-jar "你的AndroidSDK/platforms/android-33/android.jar"
```

build.py 使用 javac 编译真实 Android API、生成 classes.jar/AAR，并更新 UPM 内置 AAR；ZIP 时间固定，同工具链及相同源码应产生相同哈希。`android/build` 不入库，UPM 内置 AAR 随包版本提交。升级时不可只改 C# 或只替换 AAR。

Unity 验证入口为 `tests/AndroidBridgeUnityChecks.cs`：放入只安装核心与 Android 包的隔离工程 Assets/Editor，设置 Android 构建目标及 ERDON_PLATFORM_ANDROID 后，以 `-executeMethod AndroidBridgeUnityChecks.Run` 批处理运行。该检查覆盖真实 Unity JSON 解析/序列化及工厂、配置、AAR importer，但 JNI 接收方使用注入替身，不是真机。

Java 测试覆盖模块关闭不创建、重复/迟到回调、取消/超时、关闭后派发抑制、空结果、所有权及日志脱敏。Android 设备上的系统分享、进程重启持久化、IL2CPP/JNI/R8、渠道登录广告支付和最终 APK/AAB 裁剪尚需验收。
