# 支付与授权异常的管理员恢复

这些入口用于平台响应丢失、发货通知缺失或人工核查后的恢复，不对游戏客户端开放。正常请求继续使用平台官方验签；管理员凭证签名是内部审核授权，**不等于平台支付签名**。拥有管理员签名密钥的人有补账权限，必须与小游戏、应用密钥分开保管。

## 恢复依据

先核对平台后台订单、平台提供的账单或支持工单，确认账号、应用、环境、商品、金额和交易号。保存原始材料，计算 SHA-256，填写审核人和来源编号。网页查询的单一 success/paid、客户端截图、收银台成功回调、令牌无效错误都不足以证明付款或授权撤销。

没有足够证据时保持订单 Pending/Created 或撤销 Uncertain，不自动重新扣款，不凭猜测发货。此时缺的是外部核查结果，不是让 SDK 绕过校验。

## 凭证结构

管理员工具支持 HMAC-SHA256 封装，原始 payload 例如：

```json
{
  "schemaVersion": 1,
  "kind": "payment-confirmed",
  "evidenceId": "unique-review-id",
  "reviewer": "operator-id",
  "sourceReference": "platform-receipt-or-support-case",
  "sourceSha256": "原始材料的64位小写SHA256",
  "issuedAt": 0,
  "expiresAt": 0,
  "platform": "Douyin",
  "appId": "your-app",
  "gameId": "your-game",
  "environment": "test",
  "subject": "平台OpenId",
  "orderId": "游戏订单UUID",
  "productId": "sword",
  "amountMinor": 600,
  "currency": "CNY",
  "platformOrder": "平台侧业务订单号",
  "transactionId": "平台付款交易号",
  "platformCurrency": "CNY"
}
```

示例中的时间和哈希是待填字段，不能直接使用。时间使用 Unix 毫秒；有效期最多一天，过期拒绝；证据编号全局唯一。`subject` 是服务端的账号标识，不是邮箱或客户端自报用户名。

- 微信：`platformOrder` 必须是预订单中保存的 32 字符业务单号，另填真实 `transactionId` 与 `isSandbox`。价格/币种是已存订单金额。
- 抖音：`platformOrder` 等于游戏订单 UUID，另填渠道交易号；Android `platformCurrency=CNY`，iOS 客服支付为 `DIAMOND`。`amountMinor/currency` 始终对应本地服务器目录的人民币分。
- TikTok：`platformOrder` 与 `transactionId` 都填平台 trade_order_id；增加 `isSandbox` 和 `tokenAmount`，必须匹配持久化预支付记录。

`kind=prepayment-associated` 用于 TikTok 创建结果丢失但通过平台核查找回单号：填写相同订单字段、`requestHash`（数据库中该订单的创建请求哈希）、平台单号和代币数量，并停止旧创建处理者。它只恢复 Ready 映射，**不标记 Paid，不发货，也不再次向平台创建订单**。之后仍需官方通知或独立的 payment-confirmed 审核。

## 命令行预览与执行

配置 `SDK_OPERATOR_KEY_HEX` 为独立生成的 32 字节密钥的 64 位十六进制；`SDK_OPERATOR_KEY_ID` 默认 `operator-v1`。在完成核查的管理员环境中运行：

```powershell
# 签名已审核的 payload；输出包含账号/订单资料，应作为敏感管理文件保存。
node server/src/recovery-cli.mjs sign reviewed-payload.json > reviewed-envelope.json

# 默认只预览，不更新订单或发货；首次运行会初始化恢复审计表。
node server/src/recovery-cli.mjs payment server/config/game.local.json reviewed-envelope.json

# 审核预览后才显式写入。仅修改本地订单，真实发货由既有幂等工作器完成。
node server/src/recovery-cli.mjs payment server/config/game.local.json reviewed-envelope.json --apply

# TikTok 预下单关联还必须确认旧处理者已停止。
node server/src/recovery-cli.mjs payment server/config/game.local.json reviewed-envelope.json --previous-worker-stopped --apply
```

工具只打开已有订单数据库，不启动 HTTP，不调用平台，不自动发货。需要配置中的 App ID 环境变量与数据库路径；无需平台密钥。每次入账和审核记录在同一个 SQLite 事务内提交。重复同一凭证幂等；同一编号换内容、跨账号/游戏/环境、金额不符、交易号冲突均拒绝。`recovery.inspect(orderId)` 返回订单与审核记录，便于查账。

## TikTok 授权撤销的人工确认

默认不启用人工恢复。服务端组合时注入可信验证器：

```javascript
const verifyRecoveryEvidence = createOperatorEvidenceVerifier({
  keys: new Map([['operator-v1', Buffer.from(process.env.SDK_OPERATOR_KEY_HEX, 'hex')]])
});
const app = createTikTokApplication({ ...configuration, verifyRecoveryEvidence });
const job = app.inspectAuthorizationRevocation(subject);
// 核实平台已撤销、旧处理进程已停止之后，提交审核凭证。
await app.resolveAuthorizationRevocation(subject, {
  jobId: job.jobId, evidence: signedEnvelope, previousWorkerStopped: true
});
```

导入来自 `server/src/operator-evidence.mjs` 和 `server/src/tiktok-application.mjs`。撤销凭证 `kind=authorization-revoked`，包含公共审核字段、绑定/subject 和准确 `jobId`，不需要订单金额字段。它将 Sending/Uncertain 事务推进到 Confirmed 并保存审计，然后仅执行本地会话及令牌清理，不再次调用平台撤销。旧任务凭证不会删除后续新登录。未配置验证器返回 `RECOVERY.NOT_CONFIGURED`。

同 Client Key、同 OpenId 的游戏/环境必须共享凭据库和认证库，以及同一套加密密钥。撤销会冻结该应用下所有绑定，成功后清除这些绑定的两种令牌和会话；不同应用或账号不受影响。分属独立数据库的部署需集中认证服务；此工具不协调未知远程数据库。

凭据库现为 schema 4。升级前停掉所有旧实例、备份数据库与密钥，并完成旧版并行撤销任务的核查。发现同应用账号存在多个旧版未结束任务时拒绝使用，不能靠选一个成功任务掩盖其他不确定任务。

## 错误与审计

| 错误码 | 处理 |
|---|---|
| `RECOVERY.EVIDENCE_INVALID` | 签名、时间、密钥或审核元数据无效，重新核查生成 |
| `RECOVERY.EVIDENCE_MISMATCH` | 平台材料与本地不可变订单/授权任务不匹配，不执行 |
| `RECOVERY.EVIDENCE_REUSED` | 同证据编号用于不同内容，检查审计记录 |
| `RECOVERY.WORKER_STOP_REQUIRED` | 停止旧预下单处理者后显式确认 |
| `PAY.TRANSACTION_REUSED` / `PAY.TRANSACTION_CONFLICT` | 交易归属冲突，需要人工调查 |

审计只保存来源编号、原始材料哈希、审核人、凭证哈希、订单/任务编号和执行时间，不保存密钥或平台令牌。原始证据由运营侧另外归档。测试使用模拟账单和临时库，不表示已完成真实平台对账。
