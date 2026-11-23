# 存储能力（开发预览）

上层使用 SDK.SetStorageSync / GetStorageSync / RemoveStorageSync；底层 IStorage 实现按平台替换。这三个方法同步返回 StorageResult，必须在主线程调用。已提供 Editor 内存 Mock、[微信适配](wechat.md) 、[TikTok 适配](tiktok.md) 和 [抖音适配](douyin.md)，平台实现尚未经过真机验收。

配置面板选择 Mock 平台并勾选“存储”，进入 Play Mode 后点击“按配置初始化 Mock”，即可填写 Key/Value 并写入、读取、删除。勾选改变后重新初始化生效；SDK 保存配置快照，不受后续字段修改影响。每次初始化/退出会清空 Mock 数据，不等同于存档持久化。Mock 位于 Editor 程序集，不进入 Player。

## 接口示例

```csharp
using ErDon.UnitySDK;

// 值统一为字符串。对象请先由游戏序列化为 JSON，SDK 不依赖游戏数据类型。
StorageResult saved = SDK.SetStorageSync("my-game.settings.music", "on");
if (!saved.Success) {
    UnityEngine.Debug.Log(saved.Error.Code + ": " + saved.Error.Reason + " callId=" + saved.CallId);
}

StorageResult loaded = SDK.GetStorageSync("my-game.settings.music");
if (loaded.Success) {
    bool musicEnabled = loaded.Value == "on";
    UnityEngine.Debug.Log("音乐开关：" + musicEnabled);
    // 应用设置。
} else if (loaded.Error.Code == "STORAGE.NOT_FOUND") {
    // 使用游戏默认设置；不存在与空字符串不同。
} else {
    UnityEngine.Debug.LogWarning(loaded.Error.Code + ": " + loaded.Error.Reason
        + " callId=" + loaded.CallId);
}

StorageResult removed = SDK.RemoveStorageSync("my-game.settings.music");
if (!removed.Success) {
    UnityEngine.Debug.LogWarning(removed.Error.Code + ": " + removed.Error.Reason
        + " callId=" + removed.CallId);
}
// 删除不存在的键也成功，便于幂等清理。
```

Key 必须非空白，Set 的 value 不能是 null，允许空字符串。接口不承诺加密、云同步、跨设备或卸载后保留；不要把客户端存储作为支付或服务端身份依据。平台配额、字符串大小和持久化时机由各适配器遵守官方限制，尚未完成真实平台适配时不宣称支持这些行为。

## 初始化层与多态

初始化层将 `SdkConfig.StorageEnabled=true`，向 UnitySdkHost.Initialize 的可选 storageProvider 参数提供 IStorage。普通游戏业务不持有具体 provider。关闭模块时装配器不创建它；即使手动传入，SDK 也不会调用其 Set/Get/Remove。SDK 拥有 provider，重新初始化/Shutdown 时释放，同一个对象同时实现 IAuth/IStorage 时只释放一次。

IStorage.Get 不存在时返回 `StorageResult.Fail("STORAGE.NOT_FOUND", "...")`，成功值不得为 null；Remove 应幂等。Provider 不得重入 SDK 操作。非空失败原因由 provider 提供，SDK 日志只记录错误码，避免任意正文泄露数据。

## 错误与日志

| 错误 | 原因 |
| --- | --- |
| SDK.NOT_INITIALIZED | 尚未初始化 |
| MODULE.DISABLED | 未启用存储 |
| CAPABILITY.NOT_SUPPORTED | 没有安装存储实现 |
| STORAGE.INVALID_ARGUMENT | 键为空白或写入值为 null |
| STORAGE.NOT_FOUND | 键不存在 |
| STORAGE.INVALID_RESULT | Provider 返回 null 或读取成功但值为 null |
| STORAGE.PROVIDER_FAILED | Provider 同步抛异常 |

StorageResult.CallId 与 Error.CallId 关联 SDK 日志，Error.Operation 为实际接口名、Stage 为 Storage。日志由 SDK 生成，带时间、接口、唯一 ID、参数长度及结果，不输出真实 key/value。上层仍只接收完整日志字符串。

`tests/run-all-tests.ps1` 验证聚合调用、多态、开关、脱敏和所有权；`SdkPlayChecks` 验证配置驱动的 Editor 内存读写/删除/关闭。不等价于真实平台存储验收。
