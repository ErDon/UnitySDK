# TikTok 平台授权撤销

`createTikTokApplication` 新增受认证的 `POST /v1/authorization/revoke`，将平台撤销、游戏会话失效、加密访问/刷新令牌删除串联起来。使用现有 Client Key、Client Secret 和凭据库配置，不增加客户端密钥。

协议依据 [TikTok Minis OAuth 撤销接口](https://developers.tiktok.com/docs/zh-Hans/minis-oauth)：仅服务端调用 `/v2/oauth/revoke/`，成功必须收到空响应或空 JSON 对象；网络错误或错误响应不能当作成功。本轮验证使用模拟上游，没有撤销真实用户授权。

## 调用方式

对自己的 HTTPS 游戏后端发送：

```http
POST /v1/authorization/revoke
Authorization: Bearer <当前游戏会话>
Content-Type: application/json

{}
```

成功响应包含 `revoked: true`、`scope: "tiktok-authorization"` 和 `callId`。身份来自游戏会话，令牌来自服务器加密库；正文不能指定要撤销的用户、令牌、应用或其他绑定。已有 `/v1/session/revoke-all` 仍只撤销本地游戏会话，不调用平台。

此路由目前装配在具有凭据库的 TikTok 支付组合服务中。配置为仅登录时没有凭据库，因此没有平台撤销路由；微信、抖音也不提供此 TikTok 路由。Unity 暂无专门的撤销聚合接口，游戏可通过自己的后端请求层调用。

平台撤销作用于 TikTok 用户对应用的授权，不只是一个游戏环境。本功能按 Client Key 协调共享数据库内的多个游戏/环境绑定。所有相关绑定必须共享认证库、凭据库与加密密钥；独立数据库间不能假定同步撤销。

## 持久化流程

| 状态 | 含义及恢复方式 |
| --- | --- |
| Pending | 已持久化撤销意图，等待领取发送任务；可以继续处理 |
| Sending | 已领取平台请求；其他连接不能重复发送。进程丢失时结果不确定 |
| Uncertain | 平台调用失败或响应不确定；保持阻断，不自动重试 |
| Confirmed | 已持久化平台成功响应；恢复时只做本地清理 |
| Finalizing | 正在删除游戏会话及平台令牌；其他处理者不能并行清理 |
| Complete | 本地清理提交完成；允许用户随后重新授权登录 |

Pending 至 Finalizing 期间，普通会话认证、访问/刷新令牌读取及写入均被阻止。撤销入口会单独检查仍有效的原始 Bearer 会话，以便返回任务状态；它不允许普通业务绕过阻断。最后清理成功才返回 HTTP 200。

登录开始时读取凭据库撤销序号，落库时比较目标用户版本；撤销期间启动或撤销前尚未返回的登录，不能迟到写回令牌。刷新同时受撤销状态和原有 nonce 比较约束。完成后的新授权可以正常保存，旧任务重复完成不会删除新令牌。

已经通过认证并开始执行的业务请求不能强行回滚；平台端同时发生的新授权也不受本地事务控制。撤销不取消支付、不退款，已核验付款的通知及幂等发货仍按原流程处理。

## 恢复接口

恢复是服务端受信接口，不开放 HTTP 路由：

```javascript
// app 为 createTikTokApplication(...) 的返回对象。
// subject 必须来自服务端受信记录，不能由客户端指定。
await app.resumeAuthorizationRevocation(subject);
```

Pending 可以继续发送，Confirmed 可以完成本地清理，Complete 返回完成。对于 Sending、Uncertain、Finalizing，默认拒绝恢复并返回 AUTH.REVOCATION_UNCERTAIN。

若已停止旧处理进程、排空在途调用并核对任务，可显式执行：

```javascript
await app.resumeAuthorizationRevocation(subject, { previousWorkerStopped: true });
```

该参数是调用者的操作保证，不是 SDK 检测结果。旧进程仍可能运行时不得设置。Sending/Uncertain 会重新尝试平台请求；Finalizing 只重做本地幂等清理，不重复调用平台。等待恢复任务完成后再关闭应用及数据库。

如果上次平台请求实际成功但响应丢失，重试可能被平台拒绝；SDK 仍保持 Uncertain，不把“令牌无效”猜测为成功。此情况需要平台记录核查；已提供受信管理员签名凭证恢复入口，见 [管理员恢复](operator-recovery.md)。不会凭单个布尔值或令牌无效错误强制成功。会话过期后也需要服务端恢复入口，不能要求客户端重复登录绕过阻断。

## 错误与日志

| 错误码 | 说明 |
| --- | --- |
| AUTH.SESSION_INVALID / 401 | 游戏会话无效或不属于当前绑定 |
| AUTH.REVOCATION_PENDING / 409 | 撤销正在处理；登录、订单等普通操作被阻止 |
| AUTH.LOGIN_REVOKED / 409 | 登录返回时撤销已经完成，旧登录不再允许写回令牌 |
| AUTH.REVOCATION_UNCERTAIN / 503 | 结果不确定或旧处理者可能仍在工作；需要受控恢复 |
| AUTH.REVOCATION_STORAGE_FAILED / 503 | 撤销状态或清理存储失败；不报告成功 |
| AUTH.CREDENTIAL_UNAVAILABLE / 503 | 没有可用于开始撤销的凭据 |
| AUTH.REVOCATION_NOT_FOUND / 404 | 受信恢复接口找不到指定用户任务 |

HTTP 日志使用 `Authorization.Revoke`，含时间、接口、callId、logId、阶段和脱敏参数。平台错误正文、访问令牌、刷新令牌与 Client Secret 不向客户端返回。Unity 登录/订单错误策略识别 AUTH.REVOCATION_PENDING，不自动重新付款。

## 升级与验证边界

TikTok 凭据库支持 schema 1/2/3 事务迁移为 4，包含任务、撤销序号、刷新领取和管理员证据审计表，原加密密文和密钥配置保持可读。认证库也须使用上一轮 schema 2。升级前停止旧实例、备份数据库和部署密钥，统一升级后启动；禁止旧代码混跑或直接降级。

未完成任务的加密令牌即使过期也会保留用于恢复，不参与其他用户登录触发的过期清理。Complete 后删除访问/刷新令牌；撤销状态记录保留用于拒绝迟到写入，记录数受凭据库 capacity 限制（默认 10000），需监控容量，不能在线清空序号和状态表。

已验证：两种令牌删除、多会话失效、其他用户隔离、并发请求合并、双连接任务领取、迟到登录/刷新拒绝、重启恢复、清理失败恢复、凭据库迁移、HTTP 认证与脱敏。最终服务端全套 140 项测试及 C# 公共回归通过，包含 10 项平台撤销专项。

同 Client Key 的多绑定撤销和签名管理员证据恢复已完成本机验证；相关游戏/环境必须共享认证库、凭据库及加密密钥。撤销通过 HTTPS 服务接口调用。尚未验证真实 TikTok 授权撤销，不要将本机测试作为生产或真机验收。
