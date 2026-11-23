# 统一结果与错误诊断（草案）

所有公共异步调用用统一结果表达成功、取消与失败；预期平台失败不抛未处理异常。错误码使用稳定的领域标识，发布后不复用旧码表示新原因。

| 字段 | 约定 |
|---|---|
| code | 稳定统一错误码，供游戏分支判断 |
| messageKey / message | 可本地化的玩家提示键与默认文本 |
| reason | 已知技术原因；证据不足明确写未知 |
| platform / platformCode | 平台及原始码，原始码按字符串保留 |
| platformMessage | 脱敏原始说明，不直接展示给玩家 |
| operation / stage | 操作及阶段，例如 auth.login / credential_exchange |
| retryPolicy | Never、UserAction、Delayed、Unknown |
| retryAfterSeconds | 仅在平台明确提供时设置，不猜恢复时间 |
| traceId | 本次操作追踪标识，关联客户端与服务端日志 |

## 首批错误分类

| code | 含义 | 处理原则 |
|---|---|---|
| SDK.NOT_INITIALIZED | 未初始化 | 初始化完成后调用 |
| MODULE.DISABLED | 模块关闭或未装配 | 检查构建配置 |
| CAPABILITY.NOT_SUPPORTED | 平台不支持 | 使用游戏降级流程 |
| CONFIG.INVALID_APP_ID | 确认应用标识无效 | 修正配置，不自动重试 |
| AUTH.USER_CANCELLED | 用户取消 | 不自动重试，不按系统故障告警 |
| AUTH.PERMISSION_DENIED | 授权拒绝 | 用户操作或开发者检查权限 |
| AUTH.CREDENTIAL_EXPIRED | 凭证过期 | 重新获取凭证；限制重试次数 |
| AUTH.RATE_LIMITED | 明确限流 | 遵循已知等待时间，无倒计时不杜撰 |
| AUTH.SERVER_REJECTED | 游戏服务端拒绝 | 保留经脱敏的业务子码 |
| AUTH.UNKNOWN | 未识别登录失败 | 保留原始信息，反馈排查 |
| NETWORK.TIMEOUT | 请求超时 | 是否可重试取决于操作幂等性 |
| NETWORK.UNAVAILABLE | 明确连接不可用 | 检查连接，避免无限循环 |
| PAYMENT.STATUS_PENDING | 订单结果待确认 | 服务端查单，不重复创建付款 |

这些是自定义 SDK 设计码，不是微信或 TikTok 官方错误码。具体映射必须根据对应官方 SDK 版本和实际结果建立映射表，不能凭文字模糊匹配推断账号封禁或地区限制。

## 示例结果（设计示意）

```json
{
  "status": "failed",
  "error": {
    "code": "AUTH.UNKNOWN",
    "messageKey": "sdk.auth.failed",
    "message": "登录未成功，请稍后重试或联系支持",
    "reason": "平台未提供可确定的失败原因",
    "platform": "tiktok",
    "platformCode": null,
    "platformMessage": "Please try again or log in with a different method.",
    "operation": "auth.login",
    "stage": "platform_authorization",
    "retryPolicy": "Unknown",
    "retryAfterSeconds": null,
    "traceId": "example-trace-id"
  }
}
```

## 诊断与回归

面板显示统一码、原始码、阶段、排查建议和 traceId，支持复制脱敏报告。禁止记录密码、Token、登录 code、支付签名和敏感 URL 参数；设备及账号标识按必要性控制。Mock 可选择每种失败分支。

适配器升级需回归已知码、未知码、原始码为空、重复回调、取消后回调与超时后回调。游戏只能依赖公开稳定字段，不解析平台错误字符串驱动业务。

## 主线程调度异常

SDK.Login / SDK.Share / SDK.Pay 的完成派发若遇到 mainThreadDispatch 抛异常，会记录 SDK.DISPATCH_FAILED（Dispatch 阶段、原调用 CallId），不记录异常正文。已收到的平台结果保留，在下一次主线程 SDK.Tick 中交付，不把调度失败伪装成平台失败，也不重试真实交易或平台操作。

UnitySdkHost 自动 Tick；自定义宿主必须持续在主线程 Tick。SDK 不在后台线程直接调用游戏回调。调度器先入队/执行再抛异常时，完成动作最多执行一次；Shutdown/重新初始化后的旧恢复任务被丢弃。调度器应将每个动作可靠地派发到主线程；静默丢弃动作或错误线程执行不属于本次异常恢复保证。
