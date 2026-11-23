# 公共登录参考服务（0.1 开发版）

Node.js 22+，无 npm 第三方依赖。选择 Node 是为提供可独立运行的参考后端；协议不要求游戏使用 Node，自有后端可实现同一 HTTPS 协议。

已实现按受信 game/app/environment/platform 绑定验证请求、微信 code 验证器、随机游戏会话、过期/撤销、code 并发重放拦截、HTTP 请求/上游响应大小限制及 SDK 格式化脱敏日志。没有真实微信凭据联调；内存存储、示例启动器不用于生产。

## 本地运行与测试

在本目录执行 `node --test`。测试会启动 loopback 临时端口，不访问微信服务器。平台验证器测试使用模拟 HTTP 响应；HTTP 端到端测试使用真实本地 socket，但不替代 Unity HTTPS 联调。

实际调试时设置服务端环境变量：

```powershell
$env:SDK_LOCAL_DEVELOPMENT = '1'
$env:SDK_GAME_ID = 'your-game'
$env:WECHAT_APP_ID = 'your-app-id'
# WECHAT_APP_SECRET 应由本地安全配置/密钥管理器注入，不写入仓库或客户端。
node src/start.mjs
```

服务仅监听 `127.0.0.1:8787`。通过测试环境 HTTPS 反向代理转发 `/v1/session`，然后把 HTTPS 地址传给 UnitySessionExchange。不要为了联调取消 Unity 客户端的 HTTPS 限制。示例不启用任意 Origin CORS；浏览器/小游戏域名白名单与代理允许来源需按实际测试环境配置。

POST JSON 协议见 [authentication.md](../docs/authentication.md)。配置绑定的 environment 固定为 test。客户端提交未知绑定时，在访问微信前返回 `AUTH.APP_NOT_ALLOWED`。客户端不能指定微信上游地址、密钥或信任绑定。

## 组件与扩展

`AuthService` 接收受信 bindings、按平台注册的验证函数及 store。验证函数签名：

```js
async function verify(binding, temporaryCode) {
  // 在服务端调用对应平台校验接口；失败抛 AuthError。
  return { subject: 'platform-verified-user-id' };
}
```

只有校验后非空 subject 才允许签发会话。TikTok、抖音验证器尚未实现，不使用微信协议代替。微信适配采用固定 `https://api.weixin.qq.com/sns/jscode2session`，请求 appid/secret/js_code/grant_type；暂未从无法读取的官方文档页完成最终复核，也未真实调用。已有本地契约测试不能作为平台验收证据。

会话令牌是 32 字节密码学随机值，存储仅留 SHA-256 摘要。记录包含完整绑定、平台用户标识和过期时间；不把 openid/session_key 直接作为客户端 session。业务服务调用 `authenticate(token, expectedBinding)` 校验，然后自行映射游戏账号；expectedBinding 来自业务服务受信路由配置，不能直接使用玩家提交的值。`revoke(token)` 撤销该会话，不影响其他会话。会话默认一小时，代码重放记录默认十分钟，可按平台策略配置。

共享存储实现必须提供以下异步接口：

| 方法 | 必须满足的语义 |
| --- | --- |
| claim(hash, expiresAt) | 全实例原子 create-if-absent；未过期记录存在返回 false；新记录写入返回 true；失败抛错 |
| putSession(hash, record) | 持久写入后才 resolve；记录包含 binding、subject、expiresAt |
| getSession(hash) | 不存在/过期返回 null；不能返回已过期记录 |
| deleteSession(hash) | 幂等撤销 |

重放键按 platform/appId/code 隔离，防止同一平台应用在不同游戏/环境绑定重复消费同一个 code。即使上游超时也保留 claim，因为上游可能已经消费；客户端应重新获取 code。记录从不包含原始 code。MemoryAuthStore 有容量限制与访问时过期清理，但进程重启数据丢失、多实例不共享，故只能用于测试。

## HTTP 与诊断

成功 `200 {session,expiresAt,callId}`；失败 `{errorCode,callId}`。不向客户端输出上游 errMsg、密钥 URL、异常堆栈或平台 session_key。

| 状态/错误 | 原因 |
| --- | --- |
| 400 REQUEST.INVALID | 请求格式或字段无效 |
| 403 AUTH.APP_NOT_ALLOWED | 未配置的绑定 |
| 409 AUTH.CODE_REUSED | 同应用临时 code 已经提交 |
| 401 AUTH.PLATFORM_REJECTED | 微信返回非零错误码；尚未细分具体平台错误 |
| 502 AUTH.PLATFORM_UNAVAILABLE | 上游 HTTP/连接/超时失败 |
| 502 AUTH.PLATFORM_RESPONSE_INVALID | 非法 JSON、超限或缺少必要字段 |
| 503 SERVER.CAPACITY | 内存存储达到上限 |

日志由组件生成包含 time/interface/callId/logId/stage/params/result 的 JSON 行；可提供 `log(line)` 输出回调。参数整体脱敏，日志回调异常不影响登录结果。客户端 CallId 和服务端 callId 当前独立生成，跨端 trace 传播仍待实现。Unity HTTP 适配目前将多数非 200 错误映射为 AUTH.SERVER_REJECTED；细化服务端错误到客户端的映射仍待完成。

发布前还需共享存储实现、部署级限流、HTTPS/域名配置、真实平台校验、跨端错误诊断、Unity 联调及运维验收。支付、事件与发货接口不在此增量内，仍属于总体计划待办。
