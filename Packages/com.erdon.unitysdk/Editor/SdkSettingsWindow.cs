using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ErDon.UnitySDK.Editor
{
    public enum DistributionPlatform { Mock, WeChat, Douyin, TikTok, AndroidChannel, IOSChannel }
    [FilePath("ProjectSettings/ErDonSdk.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class SdkProjectSettings : ScriptableSingleton<SdkProjectSettings>
    {
        public DistributionPlatform platform = DistributionPlatform.Mock;
        public bool production;
        public bool login = true;
        public bool rewardedAds;
        public bool payments;
        public bool share;
        public bool storage;
        public bool network;
        public bool analytics;
        public string gameId = "";
        public string appId = "";
        public void SaveSettings() { Save(true); }
        public SdkRuntimeConfiguration CreateRuntimeConfiguration()
        {
            return new SdkRuntimeConfiguration { platform = platform.ToString(), production = production,
                gameId = gameId, appId = appId, login = login, storage = storage, share = share,
                payments = payments, rewardedAds = rewardedAds, network = network, analytics = analytics };
        }
        public List<string> Validate()
        {
            var issues = new List<string>();
            if (string.IsNullOrWhiteSpace(gameId)) issues.Add("缺少游戏标识 Game ID。");
            if (production && platform == DistributionPlatform.Mock) issues.Add("生产环境禁止 Mock。");
            if (platform != DistributionPlatform.Mock) {
                issues.Add("真实平台构建依赖与产物验收尚未完成；当前不允许真实平台构建。");
                if (string.IsNullOrWhiteSpace(appId)) issues.Add("缺少平台应用标识 App ID。");
            }
            if (analytics || (network && platform != DistributionPlatform.Mock))
                issues.Add("已勾选尚未实现的能力。当前提供 Editor Mock 登录、存储、分享、广告、网络和支付示例。");
            return issues;
        }
    }
    public sealed class SdkSettingsWindow : EditorWindow
    {
        Vector2 scroll;
        MockLoginOutcome outcome;
        MockPaymentOutcome paymentOutcome;
        MockRewardedAdOutcome adOutcome;
        MockNetworkOutcome networkOutcome;
        INetworkOperation lastNetworkOperation;
        string lastResult = "尚未调用";
        string storageKey = "preview.key", storageValue = "preview-value";
        [MenuItem("Tools/ErDon SDK/配置与预览")]
        public static void Open() { GetWindow<SdkSettingsWindow>("SDK 配置"); }
        void OnGUI()
        {
            var settings = SdkProjectSettings.instance;
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.HelpBox("开发预览版：支持 Mock 预览及平台 SDK 初始化；真实平台构建验收仍待完成。", MessageType.Info);
            EditorGUI.BeginChangeCheck();
            settings.gameId = EditorGUILayout.TextField("Game ID", settings.gameId);
            settings.platform = (DistributionPlatform)EditorGUILayout.EnumPopup("发行平台", settings.platform);
            settings.production = EditorGUILayout.Toggle("生产环境", settings.production);
            settings.appId = EditorGUILayout.TextField("App ID（非密钥）", settings.appId);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("能力模块", EditorStyles.boldLabel);
            settings.login = EditorGUILayout.Toggle("登录", settings.login);
            settings.rewardedAds = EditorGUILayout.Toggle("激励广告（Editor Mock）", settings.rewardedAds);
            settings.payments = EditorGUILayout.Toggle("支付（Editor Mock）", settings.payments);
            settings.share = EditorGUILayout.Toggle("分享（Editor Mock）", settings.share);
            settings.storage = EditorGUILayout.Toggle("存储（Editor Mock）", settings.storage);
            settings.network = EditorGUILayout.Toggle("网络（Editor Mock）", settings.network);
            settings.analytics = EditorGUILayout.Toggle("数据上报（待实现）", settings.analytics);
            if (EditorGUI.EndChangeCheck()) settings.SaveSettings();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("配置预览", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("核心包", "com.erdon.unitysdk 0.1.0");
            EditorGUILayout.LabelField("平台实现", settings.platform == DistributionPlatform.Mock ? "Editor 示例；非真实服务" : "按平台包自动注册；需安装官方依赖");
            EditorGUILayout.LabelField("登录开关", settings.login ? "Mock 初始化时启用" : "Mock 初始化时关闭");
            EditorGUILayout.HelpBox("官方依赖需按目标平台独立安装。此预览不是依赖安装检查或构建产物裁剪证明。", MessageType.Info);
            foreach (var issue in settings.Validate()) EditorGUILayout.HelpBox(issue, MessageType.Error);
            foreach (var issue in SdkBuildGuard.CheckDefines(settings.platform.ToString())) EditorGUILayout.HelpBox(issue, MessageType.Error);
            foreach (var issue in PlatformDependencyCatalog.CheckVendorInstallations(settings.platform.ToString())) EditorGUILayout.HelpBox(issue, MessageType.Error);
            if (GUILayout.Button("检查并保存配置")) {
                settings.SaveSettings();
                var issues = settings.Validate();
                EditorUtility.DisplayDialog("SDK 配置检查", issues.Count == 0 ? "配置检查通过；仅适用于当前 Editor 示例范围。" : string.Join("\n", issues), "确定");
            }
            if (GUILayout.Button("导出运行时配置 JSON（不会初始化平台）")) {
                var path = EditorUtility.SaveFilePanel("导出 SDK 配置", "", "erdon-sdk-config", "json");
                if (!string.IsNullOrEmpty(path)) {
                    System.IO.File.WriteAllText(path, JsonUtility.ToJson(settings.CreateRuntimeConfiguration(), true));
                    lastResult = "配置已导出；需要在平台初始化成功后加载，构建校验仍然生效。";
                }
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("真实平台启动（仅 Play Mode）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("需安装对应官方 SDK 和适配包并启用平台宏。这里只启动平台，不展示广告或发起支付；Editor 能力取决于官方 SDK。构建阻断仍生效。", MessageType.Info);
            EditorGUILayout.LabelField("登录后端工厂", EditorPlatformBootstrap.SessionExchangeFactory == null ? "未注入（关闭登录可测试启动）" : "已注入");
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || settings.platform == DistributionPlatform.Mock)) {
                if (GUILayout.Button("按配置启动平台 SDK")) {
                    try {
                        lastResult = "正在等待平台初始化";
                        EditorPlatformBootstrap.Begin(result => {
                            lastResult = (result.Success ? "平台初始化与能力装配成功（非真机验收）" : result.Error.Code + " / " + result.Error.Reason)
                                + " callId=" + result.CallId;
                            Repaint();
                        });
                    } catch (InvalidOperationException ex) {
                        lastResult = ex.Message.StartsWith("CONFIG.", StringComparison.Ordinal) ? ex.Message : "平台启动配置失败，检查平台包和后端工厂。";
                    } catch { lastResult = "平台启动失败，检查配置字段和平台包。"; }
                }
                if (GUILayout.Button("取消平台启动等待")) UnitySdkHost.CancelStartup();
                if (GUILayout.Button("关闭平台 SDK")) { UnitySdkHost.Shutdown(); lastResult = "已关闭平台 SDK"; }
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Mock 调试（仅 Play Mode）", EditorStyles.boldLabel);
            outcome = (MockLoginOutcome)EditorGUILayout.EnumPopup("模拟结果", outcome);
            paymentOutcome = (MockPaymentOutcome)EditorGUILayout.EnumPopup("支付模拟结果", paymentOutcome);
            networkOutcome = (MockNetworkOutcome)EditorGUILayout.EnumPopup("网络模拟结果", networkOutcome);
            adOutcome = (MockRewardedAdOutcome)EditorGUILayout.EnumPopup("广告模拟结果", adOutcome);
            EditorGUILayout.HelpBox("每次初始化读取当前配置；修改模块开关后重新初始化。超时模拟使用 1 秒；其他调用使用默认超时。", MessageType.Info);
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || settings.Validate().Count != 0)) {
                if (GUILayout.Button("按配置初始化 Mock")) {
                    try { EditorSdkBootstrap.Initialize(outcome, payment: paymentOutcome, ads: adOutcome, network: networkOutcome); lastResult = "Mock 已初始化"; }
                    catch (Exception ex) { lastResult = ex.Message; }
                }
                using (new EditorGUI.DisabledScope(SDK.ActivePlatform != "Mock")) {
                if (GUILayout.Button("调用 SDK.Request（不会联网）")) {
                    lastNetworkOperation = SDK.Request(new NetworkRequest("https://mock.invalid/preview", timeoutSeconds: 1), result => {
                        lastResult = (result.Success ? "Mock HTTP " + result.StatusCode : result.Error.Code) + " callId=" + result.CallId;
                        Repaint();
                    });
                }
                if (GUILayout.Button("取消最近一次网络请求") && lastNetworkOperation != null) lastNetworkOperation.Cancel();
                if (GUILayout.Button("调用 SDK.ShowRewardedAd（不展示真实广告）")) {
                    SDK.ShowRewardedAd(new RewardedAdRequest("mock-ad-unit"), result => {
                        lastResult = (result.Success ? (result.Completed ? "Mock 完整观看（非发奖凭证）" : "Mock 提前关闭") : result.Error.Code) + " callId=" + result.CallId;
                        Repaint();
                    }, adOutcome == MockRewardedAdOutcome.Timeout ? 1000 : 120000);
                }
                if (GUILayout.Button("取消本地广告等待")) SDK.CancelRewardedAd();
                if (GUILayout.Button("调用 SDK.Share（仅模拟发起）")) {
                    SDK.Share(new ShareRequest("Mock share"), result => {
                        lastResult = (result.Success ? "Mock 已发起，非真实分享" : result.Error.Code) + " callId=" + result.CallId;
                        Repaint();
                    });
                }
                if (GUILayout.Button("调用 SDK.Pay（不产生真实订单）")) {
                    SDK.Pay(new PaymentRequest("mock-session", "mock-product", "mock-purchase"), result => {
                        lastResult = (result.Success ? "Mock 已发货（非真实支付）" : result.Error.Code) + " callId=" + result.CallId;
                        Repaint();
                    }, paymentOutcome == MockPaymentOutcome.Timeout ? 1000 : 60000);
                }
                if (GUILayout.Button("取消本地支付等待")) SDK.CancelPay();
                if (GUILayout.Button("调用 SDK.Login")) {
                    SDK.Login(result => {
                        lastResult = (result.Success ? "Mock 成功（非真实会话）" : result.Error.Code) + " callId=" + result.CallId;
                        Repaint();
                    }, outcome == MockLoginOutcome.Timeout ? 1000 : 15000);
                }
                if (GUILayout.Button("取消当前登录")) SDK.CancelLogin();
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("内存存储预览（初始化/退出后清空）", EditorStyles.boldLabel);
                storageKey = EditorGUILayout.TextField("Key", storageKey);
                storageValue = EditorGUILayout.TextField("Value", storageValue);
                if (GUILayout.Button("写入 SDK.SetStorageSync")) ShowStorage(SDK.SetStorageSync(storageKey, storageValue));
                if (GUILayout.Button("读取 SDK.GetStorageSync")) ShowStorage(SDK.GetStorageSync(storageKey));
                if (GUILayout.Button("删除 SDK.RemoveStorageSync")) ShowStorage(SDK.RemoveStorageSync(storageKey));
                if (GUILayout.Button("释放 SDK")) { UnitySdkHost.Shutdown(); lastResult = "已释放"; }
                }
            }
            EditorGUILayout.LabelField(lastResult, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndScrollView();
        }
        void ShowStorage(StorageResult result) {
            lastResult = (result.Success ? "存储成功" : result.Error.Code) + " callId=" + result.CallId;
            if (result.Success && result.Value != null) storageValue = result.Value;
        }
    }
}
