# 数据上报（契约阶段）

当前已提供 `AnalyticsEvent`、`AnalyticsResult`、`IAnalytics` 和可选工厂接口。尚未接入 SDK 聚合入口、平台实现或配置面板；运行时配置中的 analytics 开关仍被拒绝，不应在生产中启用。

## 参数与结果

事件使用独立快照，上层修改原始字典或读取到的字典不影响事件。允许 String、Int32、有限 Double 值；不接受 null、集合、自定义对象、布尔值或其他数字类型。需要其他类型时由业务显式转换并与平台后台事件定义保持一致。

SDK 自身限制：事件名和参数名为 1–64 个 ASCII 字母、数字或下划线；每个事件最多 32 个参数，字符串值最多 1024 个 UTF-16 字符。这些是公共契约限制，不代表平台配额或后台配置要求。平台适配仍需验证目标平台的额外要求。

```csharp
var analyticsEvent = new AnalyticsEvent("level_end",
    new Dictionary<string, object> {
        { "level", 4 },
        { "duration", 12.5 },
        { "mode", "solo" }
    });
// 此阶段仅构造事件；SDK.ReportEvent 聚合入口尚未提供。
```

本地微信官方包 0.1.23 的 `Runtime/WX.cs` 中，`WX.ReportEvent<T>(string eventId, T data)` 返回 void，未提供上报接收确认回调；注释标明最低基础库 2.14.4。对应文档入口为 [wx.reportEvent](https://developers.weixin.qq.com/minigame/dev/api/data-analysis/wx.reportEvent.html)（本轮在线读取未成功，以本地签名为依据）。

因此 `AnalyticsResult.Dispatched` 只表示平台调用正常返回，不能证明数据送达、后台接受或报表入库。不为其提供有歧义的 Success 字段。错误结果包含统一 `SdkError`；CallId/TraceId 预留给后续聚合入口关联。不得据此结果结算奖励或确认支付，不自动重试以免产生重复统计。

## 已验证范围

`tests/AnalyticsContractTests.cs` 覆盖事件字典隔离、空参数、无效名称、数量和长度边界、非有限数字和不支持的类型、派发结果语义。通过公共回归入口 `tests/run-all-tests.ps1` 执行。尚无平台调用或真实数据入库验证。
