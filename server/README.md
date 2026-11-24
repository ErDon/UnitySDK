# 公共登录参考服务（0.1 开发版）

推荐使用新的 [账号与密钥配置入口](../docs/server-configuration.md)：三平台 JSON 模板 + 本地 .env，`npm run config:check -- config/douyin.local.json` 检查，`npm run start:configured -- config/douyin.local.json` 启动。未填真实密钥可先完成离线检查与自动测试。

Node.js 24.20.0+（本机验证版本 24.20.0），无 npm 第三方依赖。选择 Node 是为提供可独立运行的参考后端；协议不要求游戏使用 Node，自有后端可实现同一 HTTPS 协议。

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

只有校验后非空 subject 才允许签发会话。已有独立微信、抖音和 TikTok 验证器，按受信绑定选择，不使用微信协议代替其他平台。微信适配采用固定 `https://api.weixin.qq.com/sns/jscode2session`，请求 appid/secret/js_code/grant_type；暂未从无法读取的官方文档页完成最终复核，也未真实调用。抖音适配及官方依据见 [douyin.md](../docs/douyin.md)。已有本地契约测试不能作为平台验收证据。

本地启动器可分别用 WECHAT_APP_ID/WECHAT_APP_SECRET 或 DOUYIN_APP_ID/DOUYIN_APP_SECRET 配置一个或两个平台，公用 SDK_GAME_ID；至少配置一个完整应用绑定。创建服务后只按明确匹配的绑定路由到对应验证器。

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

日志由组件生成包含 time/interface/callId/logId/stage/params/result 的 JSON 行；可提供 `log(line)` 输出回调。参数整体脱敏，日志回调异常不影响登录结果。客户端 CallId 和服务端 callId 当前独立生成；客户端保留 ServerCallId 并写入完成日志以关联两端。Unity HTTP 适配只映射已登记的错误码/状态组合，未知错误归为 AUTH.SERVER_REJECTED，不输出任意服务端错误正文。完整 trace 传播仍待实现。

发布前还需部署级限流、HTTPS/域名配置、真实平台校验、Unity 联调及运维验收；多机部署还需共享存储。支付、事件与发货接口仍属于总体计划待办。

## SQLite 持久化选项

`SqliteAuthStore` 使用 Node 内置 node:sqlite（同步 API），实现同一存储契约。WAL、FULL 同步提交、写事务保护 claim 的检查/插入；会话只保存令牌摘要、身份和绑定。初始化识别自身 schema 版本，未知版本拒绝启动，不自动破坏性迁移。

本地启动前可设置：

```powershell
New-Item -ItemType Directory -Force ./data | Out-Null
$env:SDK_AUTH_DB = Join-Path (Get-Location) 'data/auth.sqlite'
```

未设置 SDK_AUTH_DB 时仍使用内存存储。设置后数据库目录必须已存在并可写，重启会保留未过期会话及 code claim。`server/data/` 已加入 gitignore。数据库含平台身份信息，应限制文件访问并采用 SQLite 支持的备份方式；不能仅复制仍在写入的主文件而遗漏 WAL。

此实现支持同机多个数据库连接竞争同一文件，不将 WAL 文件放在网络共享盘。多台服务器部署应实现 Redis/SQL 等共享存储；同步 SQLite 会阻塞 Node 事件循环，当前尚无高负载性能验收。过期清理在操作时执行，schema 升级、备份恢复与容量策略仍需部署验收。

持久化测试覆盖关闭/重开、作用域、撤销、过期释放容量、关闭后的失败、四个 worker 独立连接并发 claim 和未知 schema 拒绝。它们不代表生产部署验收。

## 会话接口限流

默认每个直接连接来源每 60 秒最多 30 次请求，超限返回 HTTP 429、AUTH.RATE_LIMITED 和 Retry-After 整数秒数；限流在读取请求正文和兑换凭证前执行。客户端仅展示重试建议，不自动重试。

createAuthHandler 可注入 limiter，其 take(source) 返回或异步返回 {allowed, retryAfterSeconds}。默认 MemoryRateLimiter 使用进程随机盐 HMAC 生成来源键，不存储原始 IP；最大一万个来源键，容量耗尽返回 SERVER.CAPACITY。计数只存在本进程，重启清空。

不信任客户端 X-Forwarded-For。反向代理后的连接会共享代理地址额度；部署时应在受信网关实施客户端限流，并配置适配的服务端 limiter，不能直接信任转发头。多实例需共享限流实现；此参考实现不替代部署验收。

## TikTok 身份校验

createTikTokVerifier 按 [TikTok Minis OAuth](https://developers.tiktok.com/docs/en/minis-oauth) 使用固定 HTTPS token 接口、表单 POST，传 client_key/client_secret/code/grant_type；不传网页 OAuth 的 redirect_uri/code_verifier。SDK 回调 code 原样交给表单编码，不手动重复 URL 解码。不注入 credentialStore 时仅验证身份；TikTok 支付服务装配会先将 access_token/refresh_token 加密保存，再签发游戏会话。支付取用令牌时按需刷新并保存轮换后的 refresh_token；平台令牌不返回客户端、不写日志。详细范围见 [支付凭据管理](../docs/payment-pipeline.md)。

本地启动配置 TIKTOK_APP_ID（值是 Client Key）、TIKTOK_APP_SECRET（Client Secret）和 SDK_GAME_ID，平台名 TikTok。这些值来自服务端环境，客户端不得提供密钥。429/上游故障返回 PLATFORM_UNAVAILABLE；OAuth 错误返回 PLATFORM_REJECTED，当前不向客户端传递任意上游描述，也尚未细分全部 OAuth 错误。

后端测试使用模拟平台响应；未以真实 TikTok code 验收。配置受信绑定不等于平台应用已获准使用对应能力。

## 订单组件

新增 OrderService、MemoryOrderStore 与 SqliteOrderStore，接口、验证器及发货契约见 [订单与发货](../docs/orders.md)。可使用 createOrderHandler 显式装配订单 HTTP 路由，默认启动器尚未启用；真实平台验签仍待完成，内存订单仅用于开发。

## 多游戏共享存储回归

运行 `node --test test/multi-game.test.mjs`，分别验证 Memory 与 SQLite 存储。六个已授权绑定覆盖游戏、环境、App ID 和平台差异；即使平台用户标识、商品 ID、幂等请求 ID 相同，会话与订单仍须按绑定隔离。测试逐对拒绝跨绑定会话使用、读单及确认，并验证撤销一个会话不影响其他绑定。同平台同应用的 code 不能通过切换游戏或环境再次兑换；不同应用或平台可独立使用相同的不透明 code 文本。

这项回归使用 SQLite 内存数据库和注入的平台身份，证明服务端边界，不证明第二个实际游戏、真机登录、真实支付或生产部署。完整后端回归 `node --test` 在 Node 24.20.0 下共 39 项通过（2026-11-24）。HTTP 测试需要允许连接自身启动的本机回环端口。


## 抖音道具直购服务

`createDouyinApplication` 组合持久化登录会话、订单、受认证预支付、官方签名通知与可选发货工作器。使用独立回调 Token；未提供 deliver 时订单保持 Paid，由游戏业务完成权益入库。完整配置、HTTP 路由及安全边界见 [抖音支付接入](../docs/douyin-payments.md)。仅有本机协议/流程验证，未进行真实支付与公网部署，主动平台查单及游戏币扣币尚未实现。
