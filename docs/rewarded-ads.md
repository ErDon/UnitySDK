# 激励广告（底层适配开发中）

新增 IRewardedAds.Show(RewardedAdRequest, callback) 与 DouyinRewardedAds，参数为 AdUnitId。底层适配在主线程、官方运行时初始化成功后使用。此阶段尚未接 SDK 聚合入口、模块工厂、面板预览和统一超时，因此配置中的激励广告仍保持未实现阻断，不应作为已完成业务能力接入。

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

当前底层不提供等待超时；统一超时/取消将在聚合层接入。释放不能保证已展示的官方 UI 立即关闭。tests/run-douyin-tests.ps1 验证真实入口编译及注入广告生命周期，尚未验证真机填充、广告位授权、展示、奖励回执或平台配额。
