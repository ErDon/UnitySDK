# 网络请求

`SDK.Request(NetworkRequest, Action<NetworkResult>)` 提供跨游戏聚合入口，返回 `INetworkOperation`，可调用 Cancel。模块需启用 NetworkEnabled，并由工厂或手动初始化提供 INetwork。当前可选实现 UnityNetwork 位于 `com.erdon.unitysdk.transport.http`，核心包不依赖 UnityWebRequest。

## 初始化

新项目先安装核心包和 HTTP 包。真实平台按 [启动说明](startup.md) 调用 BeginConfiguredStartup，配置 network=true，并传入 `createNetwork: () => new UnityNetwork(host)`。host 是仍处于激活状态的 MonoBehaviour；工厂仅在平台就绪和模块开启后创建通信实例。不要返回共享的旧实例。会话交换工厂与网络工厂用途不同，不可互相替代。

单独验证 HTTP 时可直接初始化（这会替换当前 SDK，不能在已运行的游戏 SDK 上重复执行）：

```csharp
using ErDon.UnitySDK;

// this 为持续激活的 MonoBehaviour；在主线程执行。
UnitySdkHost.Initialize(
    new SdkConfig { Platform = "Mock", LoginEnabled = false, NetworkEnabled = true },
    null, networkProvider: new UnityNetwork(this));
```

该示例使用真实 HTTPS 通信，不是面板的无网络 Mock。生产环境应采用对应平台配置和启动流程，当前真实构建仍等待验收。销毁或停用通信 host 前调用 UnitySdkHost.Shutdown，避免协程停止后请求失去驱动；不要依赖外部 host 销毁自动释放 UnityNetwork。

## GET 与结果处理

```csharp
using System.Text;
using ErDon.UnitySDK;

INetworkOperation operation = SDK.Request(
    new NetworkRequest("https://example.com/", timeoutSeconds: 10,
        maxResponseBytes: 256 * 1024),
    result => {
        if (!result.Success) {
            UnityEngine.Debug.Log(result.Error.Code + ": " + result.Error.Reason
                + " callId=" + result.CallId);
            return;
        }
        if (!result.IsHttpSuccess) {
            UnityEngine.Debug.Log("HTTP " + result.StatusCode + ", CallId=" + result.CallId);
            return;
        }
        string text = Encoding.UTF8.GetString(result.Body);
        // 根据业务协议解析 text，不把响应中的会话、令牌写入日志。
    });
// 不再需要等待：operation.Cancel();
```

example.com 仅用于连接示例；实际业务应替换为自己的已配置域名。Success 表示得到有效 HTTP 响应且没有本地/传输错误，HTTP 401、404、500 也可能为 true。IsHttpSuccess 仅在 HTTP 2xx 时为 true。两者都不代表业务成功：例如 HTTP 200 内仍可能包含业务错误码，需要上层根据协议解析。HEAD/204 可没有响应体。

## JSON POST

```csharp
using ErDon.UnitySDK;

var request = new NetworkRequest(
    "https://your-api.example/events", "POST",
    System.Text.Encoding.UTF8.GetBytes("{\"level\":1}"),
    new System.Collections.Generic.Dictionary<string, string> {
        { "Content-Type", "application/json" }
    }, timeoutSeconds: 15, maxResponseBytes: 1024 * 1024);
SDK.Request(request, result => {
    if (!result.Success) UnityEngine.Debug.Log(result.Error.Code + ": " + result.Error.Reason
        + " callId=" + result.CallId);
    else if (!result.IsHttpSuccess) UnityEngine.Debug.Log("HTTP " + result.StatusCode + " callId=" + result.CallId);
});
```

your-api.example 是占位域名，不会提供业务服务。生产 JSON 应使用项目的序列化器，不拼接用户输入。通用 Request 不自动添加游戏会话、签名、幂等键或业务重试；这些由协议层负责。不要把此接口的成功结果当作登录成功或订单发货依据。

## 参数约束

| 参数 | 规则 |
| --- | --- |
| url | 绝对 HTTPS URL，无 userInfo 和 fragment；不关闭证书校验 |
| method | GET、POST、PUT、PATCH、DELETE、HEAD、OPTIONS，内部转大写；平台可能另有限制 |
| body | 最多 1 MiB；GET/HEAD 不允许非空体 |
| headers | 名称为 HTTP token；值不可含 CR/LF/NUL；大小写不敏感的重复键被拒绝 |
| timeoutSeconds | 1–120，默认15；由 SDK.Tick 和底层共同约束 |
| maxResponseBytes | 1–16 MiB，默认1 MiB；UnityNetwork 在接收时检查限额 |

构造参数非法抛 ArgumentException 或 ArgumentOutOfRangeException，尚未创建操作；上层动态配置需在构造处处理。请求和响应的字节数组、字典均为快照，修改原输入或读取结果不会改写内部值。读取 Body 会复制数组，大响应避免反复读取。

## 错误、取消和线程

| 错误码 | 含义与处理 |
| --- | --- |
| SDK.NOT_INITIALIZED / MODULE.DISABLED | 检查初始化和开关 |
| CAPABILITY.NOT_SUPPORTED | 没有提供 INetwork；工厂装配缺能力会直接拒绝初始化 |
| NETWORK.INVALID_ARGUMENT | SDK.Request 传入 null |
| NETWORK.TIMEOUT | SDK 本地等待超时；不代表服务器未执行请求 |
| NETWORK.CANCELLED | 调用者取消本地观察，并尝试中止底层请求 |
| NETWORK.UNAVAILABLE | Unity 连接失败或底层超时；当前不进一步猜测 DNS、证书或限流原因 |
| NETWORK.RESPONSE_TOO_LARGE | 接收超过指定限额，调小响应或按需调整上限 |
| NETWORK.HOST_UNAVAILABLE | UnityNetwork 的协程 host 已不可用或未激活 |
| NETWORK.INVALID_RESPONSE | 无有效响应/HTTP 状态，或处理失败 |
| NETWORK.INVALID_OPERATION | 自定义实现未返回操作句柄且尚未完成 |
| NETWORK.PROVIDER_FAILED | 同步调用异常；原始异常正文不输出 |

同一请求首个终态胜出；底层连接超时可能先返回 NETWORK.UNAVAILABLE，SDK 先超时则为 NETWORK.TIMEOUT。不会自动重试，也不跟随重定向（UnityNetwork redirectLimit=0）。不要据此请求已取消就直接重发非幂等操作。

支持多个请求并发，每次有独立 CallId/TraceId、超时和 Cancel。初始化、调用、取消、Tick、Shutdown 均在 Unity 主线程执行；有效请求结果通过主线程派发器交付，预检失败直接在调用线程回调。回调抛异常会被隔离并记录 CALLBACK.EXCEPTION。UnitySdkHost 自动驱动 Tick；暂停后恢复时继续检查超时。

Shutdown 会中止未完成操作并抑制旧结果，游戏自己关闭等待 UI。已完成的聚合句柄不再持有底层操作、回调或派发器；UnityNetwork 的底层句柄也在终态清除回调、通信实例和请求/缓冲引用。重复 Cancel 不重复完成。SDK 统一日志记录时间、接口、ID、阶段及结果，URL、查询、头与消息体全部脱敏。通用请求 CallId 不会自动写入 HTTP 请求头。

## 平台边界与验证

三平台通过可选网络工厂装配 UnityNetwork；小游戏仍需要官方 UnityWebRequest 兼容桥、平台域名白名单、协议/CORS 与请求头许可等配置。核心允许的方法或请求头不代表平台都能使用。最终小游戏网络与产物还未真机验收。

公共测试覆盖参数、快照、并发、超时、取消和所有权。隔离 Unity 测试覆盖真实本机连接超时与取消，以及公网 HTTPS GET/HEAD 和响应接收限额，见 tests/NetworkPlayChecks.cs.txt、tests/NetworkHttpsPlayChecks.cs.txt。公网测试依赖网络，仅用于隔离工程；Mock 面板不会发网络请求。这些测试不证明三平台小游戏容器行为。
