# 登录凭证交换协议（v0.1 开发草案）

游戏调用 SDK.Login；VerifiedAuth 先调用 IPlatformCredentialProvider，再调用 ISessionExchange。平台 code 不作为游戏 Session 返回，平台授权成功但服务器校验失败必须登录失败。

POST 到游戏配置的 HTTPS 会话地址，Content-Type: application/json：

```json
{"gameId":"game-a","appId":"platform-app","environment":"test","platform":"WeChat","code":"temporary-platform-code"}
```

成功：HTTP 200，`{"session":"server-issued-session"}`。Session 应为服务端生成且有有效期的会话，不应直接使用平台 code/openid。非 200 或缺失会话均不能视为成功。服务端必须从受信配置确定 game/app/environment 的有效绑定，不能信任客户端随意声明的平台应用；平台密钥只存在服务端。

响应可包含服务端生成的 `callId`（标准带连字符 UUID）。客户端将它作为 `LoginResult.ServerCallId`，失败时也放在 `SdkError.ServerCallId`，并输出到 SDK 完成日志。客户端原有 CallId/TraceId 不被服务端值覆盖；目前用这两个编号关联两端记录，尚未实现完整分布式 tracing。非法格式的服务端编号被忽略，防止任意文本进入日志。

非 200 响应可携带 `errorCode`，例如 `409 {"errorCode":"AUTH.CODE_REUSED","callId":"..."}`。客户端只接收已登记的错误码与 HTTP 状态组合，并由 SDK 生成原因和重试建议。支持应用绑定错误、临时凭证重复消费、平台拒绝/不可用/非法响应、服务器容量及请求格式错误。未知码、不匹配状态、非 JSON 错误页统一为 AUTH.SERVER_REJECTED；在未触发响应超限或连接失败的情况下，429 映射为 AUTH.RATE_LIMITED，不要求有 JSON 正文。重试建议不会触发自动重试，重新登录必须重新取得临时 code。

UnitySessionExchange 使用 HTTPS，不跟随重定向，不记录请求凭证、响应会话、任意 HTTP 错误正文。429 返回 AUTH.RATE_LIMITED；若 Retry-After 是正整数秒数，则保留为 Error.RetryAfterSeconds。缺失、非法或 HTTP 日期格式均不猜等待时间，也不自动重试。15 秒网络超时与 SDK 外层等待超时分别存在；取消外层等待不代表服务器未处理请求。Dispose 会中止本实例的活动网络请求。

当前已实现客户端编排、HTTP 传输、微信/抖音/TikTok 凭证适配，以及 [Node 参考服务](../server/README.md) 的验证器/会话/HTTP 组件。服务端测试使用模拟平台响应及内存存储；真实平台校验、生产会话持久化与 Unity HTTPS 联调尚未完成。HTTP 响应使用最多 65536 字节的固定缓冲区，在接收分块时拒绝越界并返回 NETWORK.RESPONSE_TOO_LARGE，优先于通用传输错误。该限制约束客户端保留的正文，不承诺精确限制底层网络收包量，也不能替代服务端限流。

HTTPS 实现单独位于可选 UPM 包 com.erdon.unitysdk.transport.http；核心只保留 ISessionExchange 接口，不强制引入 UnityWebRequest。自有后端协议不同可自行实现 ISessionExchange。安装传输包时同时安装本仓库核心包；两者版本应一致。

调用 Exchange 时，宿主 GameObject 未激活、组件被禁用或宿主已销毁，会立即返回 NETWORK.HOST_UNAVAILABLE，不启动请求。已经 Dispose 的客户端优先返回 SDK.DISPOSED。此检查仅覆盖发起前状态；请求发出后仍需由游戏在宿主停止/销毁前 Dispose 或调用 UnitySdkHost.Shutdown，不能依赖协程自动完成回调。


## 游戏上层登录

先按[启动说明](startup.md)完成平台初始化和后端会话工厂装配，并启用 LoginEnabled；在 Unity 主线程执行以下调用。这里取得的是游戏后端会话，不能将平台临时 code 当作登录结果。

```csharp
using ErDon.UnitySDK;

SDK.Login(result => {
    if (!result.Success) {
        UnityEngine.Debug.LogWarning(result.Error.Code + ": " + result.Error.Reason
            + " callId=" + result.CallId + " serverCallId=" + result.ServerCallId);
        // 按 Error.RetryAfterSeconds 等结构化字段提示用户；不自动循环重试。
        return;
    }
    // 将 result.Session 交给游戏的会话管理器；不要输出到日志或分析事件。
    UnityEngine.Debug.Log("游戏登录成功，callId=" + result.CallId);
}, timeoutMilliseconds: 15000);
// SDK.CancelLogin(); // 只结束本地等待，不撤销平台授权或已经创建的服务端会话。
```

未初始化返回 SDK.NOT_INITIALIZED，关闭模块返回 MODULE.DISABLED；已有登录等待时返回 AUTH.IN_PROGRESS。取消/超时不证明服务器未处理请求，重新登录会重新取得临时凭证。详细错误字段见[错误诊断](errors.md)，真实账号与 HTTPS 联调仍需单独验收。
