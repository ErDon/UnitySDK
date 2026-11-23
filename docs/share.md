# 分享能力

上层调用 SDK.Share，下层实现 IShare。当前提供公共流程、Editor Mock、微信、TikTok 和抖音分享调用适配；三个平台均已进行官方程序集编译与注入行为检查，但仍待真机验收。不能把 Mock 当作已接入平台。

## 调用

```csharp
using ErDon.UnitySDK;

SDK.Share(new ShareRequest(
    title: "一起来玩",
    imageUrl: "https://your-game.example/share.png",
    query: "scene=invite"), result => {
    if (result.Success) {
        // 仅表示分享调用已发起，不证明用户发送、好友收到或完成邀请。
        UnityEngine.Debug.Log("分享已发起，编号：" + result.CallId);
    } else {
        UnityEngine.Debug.LogWarning(result.Error.Code + ": " + result.Error.Reason
            + "，编号：" + result.CallId);
    }
}, timeoutMilliseconds: 15000);
```

Title 必须非空白；ImageUrl、Query 可省略，平台适配负责平台格式、长度和能力限制。Query 不包含开头问号，业务参数需自行 URL 编码；不要放访问令牌或个人数据。SDK 不自动读取联系人或证明送达，Success 不能作为奖励发放依据。

SDK.CancelShare() 只结束本地等待，不保证平台分享面板关闭或撤回发送。默认超时 15 秒，由 UnitySdkHost.Update 驱动 SDK.Tick；自行接入其他引擎必须定期 Tick。登录和分享分别管理正在进行的调用。同一分享等待未完成时再次调用返回 SHARE.IN_PROGRESS。

## 配置与多态

设置 SdkConfig.ShareEnabled=true，使用 UnitySdkHost.Initialize 的 shareProvider 参数传入 IShare。推荐工厂同时实现 ISdkProviderFactory 与 IShareProviderFactory，SDK 仅在 ShareEnabled 时调用 CreateShare。未启用不创建对象；启用但工厂不支持则初始化失败并回收本次创建对象。旧工厂无需实现新增接口，仍可用于关闭分享的配置。

每个 provider 必须是新对象，交付后由 SDK 释放。异步 provider 调用 callback(ShareResult.Dispatched()) 表示已发起，失败使用 ShareResult.Fail(SdkError) 保留结构化平台码。重复或释放后的回调被忽略；调用者回调异常被隔离。不得将平台原始任意错误消息、分享内容写入日志。SDK 日志提供时间、接口、调用/日志编号和阶段，分享内容整体脱敏。

## 失败处理

| Code | 原因与处理 |
| --- | --- |
| SDK.NOT_INITIALIZED | 先完成初始化 |
| MODULE.DISABLED | 启用分享配置并重新初始化 |
| CAPABILITY.NOT_SUPPORTED | 当前未提供分享实现 |
| SHARE.INVALID_ARGUMENT | 请求为空或标题为空白 |
| SHARE.IN_PROGRESS | 已有分享等待；等待完成或取消本地等待 |
| SHARE.CANCELLED | 调用者取消本地等待，不等于用户取消发送 |
| SHARE.TIMEOUT | 平台未在等待期限内完成调用，不推断发送结果 |
| SHARE.INVALID_RESULT | provider 返回空结果，检查适配实现 |
| SHARE.PLATFORM_REJECTED | 平台拒绝分享，查看结构化 PlatformCode，不推断具体原因 |
| SHARE.PROVIDER_FAILED | provider 抛异常，检查适配初始化与运行环境 |

失败包含 Operation=SDK.Share、Stage=ShareDispatch、CallId；TraceId 当前同 CallId。不自动重试分享，防止重复弹出面板或重复发送。

## 面板与验证

Tools → ErDon SDK → 配置与预览，勾选分享，进入 Play Mode，初始化 Mock，然后点击“调用 SDK.Share（仅模拟发起）”。关闭后重新初始化，再调用应得到 MODULE.DISABLED。

tests/ShareTests.cs 覆盖工厂开关、参数、并发调用、首回调胜出、主线程队列、超时/取消、旧结果失效、释放与脱敏；SdkPlayChecks.cs.txt 覆盖实际 Unity Play Mode 的分享开关。它们不代替真实平台验收。
