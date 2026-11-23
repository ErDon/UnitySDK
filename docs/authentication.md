# 登录凭证交换协议（v0.1 开发草案）

游戏调用 SDK.Login；VerifiedAuth 先调用 IPlatformCredentialProvider，再调用 ISessionExchange。平台 code 不作为游戏 Session 返回，平台授权成功但服务器校验失败必须登录失败。

POST 到游戏配置的 HTTPS 会话地址，Content-Type: application/json：

```json
{"gameId":"game-a","appId":"platform-app","environment":"test","platform":"WeChat","code":"temporary-platform-code"}
```

成功：HTTP 200，`{"session":"server-issued-session"}`。Session 应为服务端生成且有有效期的会话，不应直接使用平台 code/openid。非 200 或缺失会话均不能视为成功。服务端必须从受信配置确定 game/app/environment 的有效绑定，不能信任客户端随意声明的平台应用；平台密钥只存在服务端。

UnitySessionExchange 使用 HTTPS，不跟随重定向，不记录请求凭证、响应会话、任意 HTTP 错误正文。当前 429 返回 AUTH.RATE_LIMITED，不猜等待时间。15 秒网络超时与 SDK 外层等待超时分别存在；取消外层等待不代表服务器未处理请求。Dispose 会中止本实例的活动网络请求。

当前已实现客户端编排、HTTP 传输、微信/TikTok 凭证适配，以及 [Node 参考服务](../server/README.md) 的验证器/会话/HTTP 组件。服务端测试使用模拟平台响应及内存存储；真实平台校验、生产会话持久化与 Unity HTTPS 联调尚未完成。HTTP 响应长度在客户端收到后检查，不能作为服务端限流/响应限额的替代。

HTTPS 实现单独位于可选 UPM 包 com.erdon.unitysdk.transport.http；核心只保留 ISessionExchange 接口，不强制引入 UnityWebRequest。自有后端协议不同可自行实现 ISessionExchange。安装传输包时同时安装本仓库核心包；两者版本应一致。
