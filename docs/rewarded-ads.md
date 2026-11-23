# 激励广告（聚合接口与 Editor 预览）

新增 IRewardedAds.Show(RewardedAdRequest, callback) 与 DouyinRewardedAds，参数为 AdUnitId。底层适配在主线程、官方运行时初始化成功后使用。已接 SDK 聚合入口、抖音模块工厂及统一超时；面板已提供 Editor Mock 预览，运行时 JSON 对 Mock/微信/抖音/TikTok映射广告开关。三平台已提供自动装配并完成隔离 Development Build；正式生产构建和真机广告验收仍未完成。

抖音 6.9.7 使用 TT.CreateRewardedVideoAd(CreateRewardedVideoAdParam)，关闭 Multiton，再订阅 OnLoad/OnError/OnClose 后 Load。加载完成调用一次 Show；关闭时根据官方 complete 布尔值返回 Completed。Error 只有数字码进入 PlatformCode，自由文本不回传。随包 ttsdk.xml 明确 IsComplete 表示播放完成；不把所有 OnClose 都视为完整观看。

RewardedAdResult.Success 表示调用正常关闭，Completed 表示客户端报告完整观看。因此提前关闭是 Success=true、Completed=false。两者都不证明服务端已授予奖励；业务不能仅凭一次回调做高价值奖励的最终结算。当前只支持一次单视频，不提供再得广告、多视频奖励。

每个 provider 同时只允许一次展示流程，重复请求返回 ADS.IN_PROGRESS。重复加载不重复 Show，终态会解除事件并 Destroy，Dispose 抑制迟到回调并释放活动实例。广告尚未请求 Show 就回调关闭返回 ADS.INVALID_STATE，不授予完成标记。官方说明激励广告只支持一个实例，因此一个游戏不要同时创建多个 provider，也不要与其他插件独立创建同类广告。

| 错误码 | 原因 |
| --- | --- |
| ADS.INVALID_ARGUMENT | 缺少广告位 ID |
| ADS.IN_PROGRESS | 当前 provider 已有广告流程 |
| ADS.CREATE_FAILED | 官方返回空实例 |
| ADS.PLATFORM_FAILED | 平台加载或广告事件报错，查看 PlatformCode |
| ADS.SHOW_FAILED | 调用展示抛出异常 |
| ADS.INVALID_STATE | 未请求展示却收到关闭事件 |
| ADS.PROVIDER_EXCEPTION | 创建、订阅或加载调用异常 |
| SDK.DISPOSED | provider 已释放 |

底层不独立计时；SDK.ShowRewardedAd 提供统一超时/取消。释放不能保证已展示的官方 UI 立即关闭。tests/run-douyin-tests.ps1 验证真实入口编译及注入广告生命周期，尚未验证真机填充、广告位授权、展示、奖励回执或平台配额。

## 聚合调用

```csharp
using ErDon.UnitySDK;

// 初始化前 config.RewardedAdsEnabled = true；使用支持 IRewardedAdsProviderFactory 的工厂。
SDK.ShowRewardedAd(new RewardedAdRequest("your-ad-unit-id"), result => {
    if (!result.Success) {
        UnityEngine.Debug.LogWarning(result.Error.Code + ": " + result.Error.Reason + " callId=" + result.CallId);
        return;
    }
    if (result.Completed) { /* 客户端报告看完，按游戏的奖励验证规则继续处理 */ }
    else { /* 提前关闭，不认定完整观看 */ }
}, timeoutMilliseconds: 120000);
```

关闭 RewardedAdsEnabled 时，工厂不会创建广告 provider，调用返回 MODULE.DISABLED。当前 WeChatProviderFactory、DouyinProviderFactory 和 TikTokProviderFactory 均支持广告按需创建。默认等待 120 秒，UnitySdkHost 自动驱动超时；SDK.CancelRewardedAd 只取消本地等待，不保证关闭平台广告 UI 或停止底层广告流程。取消/超时后底层尚有广告活动时，后续调用可能仍返回 ADS.IN_PROGRESS，直到关闭或重新初始化释放 provider。

结果含 CallId/TraceId，错误 Stage=RewardedAd、Operation=SDK.ShowRewardedAd。日志不输出广告位内容，完整观看和提前关闭分别记录 Completed/ClosedEarly。与支付/分享的并发状态独立；旧回调和重复终态不会覆盖新调用。此处仍不提供服务端奖励验证或重复领奖保护。

## 配置面板预览

1. 打开 `Tools > ErDon SDK > 配置与预览`，平台选择 Mock，填写 Game ID，勾选激励广告。
2. 进入 Play Mode，选择广告模拟结果，然后点击“按配置初始化 Mock”。每次修改模拟结果或模块开关都需要重新初始化。
3. 点击 `SDK.ShowRewardedAd` 按钮。Completed 表示模拟完整观看，ClosedEarly 表示提前关闭，LoadFailed 返回 ADS.LOAD_FAILED，Timeout 不回调，面板使用 1 秒本地超时。
4. 可点击“取消本地广告等待”验证 ADS.CANCELLED。取消勾选并重新初始化后调用返回 MODULE.DISABLED。

预览不加载广告、不产生收入、不发放真实奖励。导出的 JSON 保留 rewardedAds；ToSdkConfig 只为已实现的平台能力放行，不能作为真实平台初始化或构建完成的证明。

聚合层还会返回 ADS.TIMEOUT、ADS.CANCELLED、ADS.INVALID_RESULT（适配器返回空结果）、ADS.PROVIDER_FAILED（适配器抛出异常），以及 SDK.NOT_INITIALIZED / MODULE.DISABLED / CAPABILITY.NOT_SUPPORTED。ADS.LOAD_FAILED 目前用于 Editor Mock 失败演练，真实抖音错误保留 ADS.PLATFORM_FAILED 与平台数字码。

## 微信适配

`WeChatRewardedAds` 使用本地官方 `com.qq.weixin.minigame 0.1.23` 的 `WX.CreateRewardedVideoAd`，关闭 multiton；监听 OnError/OnClose，显式 Load 成功后 Show。Load 失败或 OnError 返回 ADS.PLATFORM_FAILED，保留 errCode；Show 的失败回调仅有文本，返回 ADS.SHOW_FAILED，不解析或输出原始文本。

随包 wx-runtime.xml 对 WXRewardedVideoAdOnCloseResponse.isEnded 的说明为完整观看后关闭。只有收到非空关闭响应且 isEnded=true 才报告 Completed；空响应返回 ADS.INVALID_RESULT。提前关闭正常返回 Completed=false。终态解绑并销毁；释放后忽略迟到回调。每次流程独立创建；不要与其他广告插件同时操作同类平台实例。

`tests/run-wechat-factory-tests.ps1` 现在同时编译并执行广告测试：使用官方 wx-runtime-editor.dll 和隔离工程生成的 Wx.dll，注入广告事件验证加载/展示顺序、重复事件、并发、提前关闭、完整观看、错误数字码及释放。它不证明真实广告位授权、填充、收入或奖励验证；这些仍需真机验收。

## TikTok 适配

`TikTokRewardedAds` 针对本地 `1.1.6-Release` 的 TTSDK Runtime/Editor 两套 DLL 编译。CreateRewardedVideoAdParam 只有 AdUnitId，RewardedAdClosedDelegate 只有 bool 参数，与抖音的参数形态不同。订阅 OnLoad/OnError/OnClose 后 Load，加载成功调用 Show；关闭回调布尔值映射 Completed，提前关闭不视为完整观看。错误保留数值码，不传递原始消息。

早期字段映射曾参考项目本地插件，后续已使用官方 1.1.6 安装包与独立工程逐文件核对（104 个文件一致），并完成 Runtime/Editor 双 DLL 的注入广告生命周期回归、开发构建和小游戏导出。证据与版本边界见[兼容矩阵](../compatibility/README.md)。这些检查不证明线上广告行为；真实客户端版本能力、广告位授权、填充、展示和奖励验证仍需真机验收。
