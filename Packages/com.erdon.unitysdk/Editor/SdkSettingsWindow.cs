using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ErDon.UnitySDK.Editor
{
    public sealed class SdkSettingsWindow : EditorWindow
    {
        Vector2 scroll;
        [SerializeField] int previewTimeoutSeconds = 10;
        MockLoginOutcome outcome;
        MockPaymentOutcome paymentOutcome;
        MockRewardedAdOutcome adOutcome;
        MockNetworkOutcome networkOutcome;
        MockAnalyticsOutcome analyticsOutcome;
        IAnalyticsOperation lastAnalyticsOperation;
        INetworkOperation lastNetworkOperation;
        string lastResult = "尚未调用";
        string storageKey = "preview.key", storageValue = "preview-value";
        [MenuItem("Tools/ErDon SDK/配置与预览")]
        public static void Open() { GetWindow<SdkSettingsWindow>("SDK 配置"); }
        void OnEnable() { minSize = new Vector2(480, 600); }
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
            settings.rewardedAds = EditorGUILayout.Toggle("激励广告", settings.rewardedAds);
            settings.payments = EditorGUILayout.Toggle("支付（Editor Mock）", settings.payments);
            settings.share = EditorGUILayout.Toggle("分享", settings.share);
            settings.storage = EditorGUILayout.Toggle("存储", settings.storage);
            settings.network = EditorGUILayout.Toggle("网络（可选通信工厂）", settings.network);
            settings.analytics = EditorGUILayout.Toggle("数据上报", settings.analytics);
            if (EditorGUI.EndChangeCheck()) settings.SaveSettings();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("配置预览", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("核心包", "com.erdon.unitysdk 0.1.0");
            EditorGUILayout.LabelField("平台实现", settings.platform == DistributionPlatform.Mock ? "Editor 示例；非真实服务" : "按平台包自动注册；需安装官方依赖");
            var enabled = new List<string>();
            if (settings.login) enabled.Add("登录");
            if (settings.rewardedAds) enabled.Add("激励广告");
            if (settings.payments) enabled.Add("支付");
            if (settings.share) enabled.Add("分享");
            if (settings.storage) enabled.Add("存储");
            if (settings.network) enabled.Add("网络");
            if (settings.analytics) enabled.Add("数据上报");
            EditorGUILayout.LabelField("启用模块", enabled.Count == 0 ? "无" : string.Join("、", enabled.ToArray()), EditorStyles.wordWrappedLabel);
            EditorGUILayout.HelpBox("预览显示下一次初始化使用的配置；修改开关后需重新初始化。关闭模块不会创建对应能力实例。", MessageType.Info);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying || EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)) {
                if (GUILayout.Button("应用构建能力选择（触发重编译）")) settings.ApplyCapabilityDefines();
            }
            EditorGUILayout.HelpBox("构建能力选择移除未勾选的五类平台适配及网络/支付通信实现；Editor 保留预览。公共接口和官方 SDK 内部代码的保留范围需单独审计。", MessageType.Info);
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
                var configuration = settings.CreateRuntimeConfiguration();
                var issues = configuration.Validate();
                if (issues.Count != 0) {
                    lastResult = "配置未导出：" + string.Join("、", issues.ToArray());
                    EditorUtility.DisplayDialog("无法导出 SDK 配置", lastResult, "确定");
                } else {
                    var path = EditorUtility.SaveFilePanel("导出 SDK 配置", "", "erdon-sdk-config", "json");
                    if (string.IsNullOrEmpty(path)) lastResult = "已取消导出；未写入文件。";
                    else {
                        try {
                            System.IO.File.WriteAllText(path, JsonUtility.ToJson(configuration, true));
                            lastResult = "配置已导出；需要在平台初始化成功后加载，构建校验仍然生效。";
                        } catch (Exception ex) {
                            if (!(ex is System.IO.IOException) && !(ex is UnauthorizedAccessException)
                                && !(ex is System.Security.SecurityException) && !(ex is ArgumentException)
                                && !(ex is NotSupportedException)) throw;
                            lastResult = "配置导出失败：无法写入所选文件，请检查目录、文件占用和写入权限。";
                            EditorUtility.DisplayDialog("SDK 配置导出失败", lastResult, "确定");
                        }
                    }
                }
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("真实平台启动（仅 Play Mode）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("需安装对应官方 SDK 和适配包并启用平台宏。这里只启动平台，不展示广告或发起支付；Editor 能力取决于官方 SDK。构建阻断仍生效。", MessageType.Info);
            EditorGUILayout.LabelField("网络工厂", EditorPlatformBootstrap.NetworkFactory == null ? "未注入（真实平台网络开启时需要）" : "已注入");
            EditorGUILayout.LabelField("登录后端工厂", EditorPlatformBootstrap.SessionExchangeFactory == null ? "未注入（关闭登录可测试启动）" : "已注入");
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying)) {
                using (new EditorGUI.DisabledScope(settings.platform == DistributionPlatform.Mock)) {
                if (GUILayout.Button("按配置启动平台 SDK")) {
                    try {
                        lastResult = "正在等待平台初始化";
                        EditorPlatformBootstrap.Begin(result => {
                            lastResult = (result.Success ? "平台初始化与能力装配成功（非真机验收）" : DescribeError(result.Error))
                                + " callId=" + result.CallId;
                            Repaint();
                        });
                    } catch (InvalidOperationException ex) {
                        lastResult = ex.Message.StartsWith("CONFIG.", StringComparison.Ordinal) ? ex.Message : "平台启动配置失败，检查平台包和后端工厂。";
                    } catch { lastResult = "平台启动失败，检查配置字段和平台包。"; }
                }
                }
                if (GUILayout.Button("取消平台启动等待")) UnitySdkHost.CancelStartup();
                if (GUILayout.Button("关闭平台 SDK")) { UnitySdkHost.Shutdown(); lastResult = "已关闭平台 SDK"; }
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Mock 调试（仅 Play Mode）", EditorStyles.boldLabel);
            outcome = (MockLoginOutcome)EditorGUILayout.EnumPopup("模拟结果", outcome);
            paymentOutcome = (MockPaymentOutcome)EditorGUILayout.EnumPopup("支付模拟结果", paymentOutcome);
            analyticsOutcome = (MockAnalyticsOutcome)EditorGUILayout.EnumPopup("上报模拟结果", analyticsOutcome);
            networkOutcome = (MockNetworkOutcome)EditorGUILayout.EnumPopup("网络模拟结果", networkOutcome);
            adOutcome = (MockRewardedAdOutcome)EditorGUILayout.EnumPopup("广告模拟结果", adOutcome);
            previewTimeoutSeconds = EditorGUILayout.IntSlider("预览等待上限（秒）", previewTimeoutSeconds, 1, 120);
            EditorGUILayout.HelpBox("修改模块开关或模拟结果后重新初始化。等待上限只影响下一次 Mock 调用，不修改运行时 JSON 或在途请求；可留出时间点击取消。", MessageType.Info);
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying)) {
                // Draft configuration validity must not prevent cancelling/releasing the active SDK.
                using (new EditorGUI.DisabledScope(settings.Validate().Count != 0)) {
                if (GUILayout.Button("按配置初始化 Mock")) {
                    try { EditorSdkBootstrap.Initialize(outcome, payment: paymentOutcome, ads: adOutcome, network: networkOutcome, analytics: analyticsOutcome); lastResult = "Mock 已初始化"; }
                    catch (Exception ex) { lastResult = ex.Message; }
                }
                }
                using (new EditorGUI.DisabledScope(SDK.ActivePlatform != "Mock")) {
                if (GUILayout.Button("调用 SDK.Request（不会联网）")) {
                    lastResult = "正在等待 SDK.Request 模拟结果；可取消最近一次网络请求。";
                    lastNetworkOperation = SDK.Request(new NetworkRequest("https://mock.invalid/preview", timeoutSeconds: previewTimeoutSeconds), result => {
                        lastResult = (result.Success ? "Mock HTTP " + result.StatusCode : DescribeError(result.Error)) + " callId=" + result.CallId;
                        Repaint();
                    });
                }
                if (GUILayout.Button("ReportEvent（不发送数据）")) {
                    lastResult = "正在等待 SDK.ReportEvent 模拟结果；可取消最近一次上报。";
                    lastAnalyticsOperation = SDK.ReportEvent(new AnalyticsEvent("preview_event"), result => {
                        lastResult = (result.Dispatched ? "模拟上报调用完成（不代表入库）" : DescribeError(result.Error)) + " callId=" + result.CallId;
                        Repaint();
                    }, previewTimeoutSeconds * 1000);
                }
                if (GUILayout.Button("取消最近一次上报") && lastAnalyticsOperation != null) lastAnalyticsOperation.Cancel();
                if (GUILayout.Button("取消最近一次网络请求") && lastNetworkOperation != null) lastNetworkOperation.Cancel();
                if (GUILayout.Button("调用 SDK.ShowRewardedAd（不展示真实广告）")) {
                    lastResult = "正在等待 SDK.ShowRewardedAd 模拟结果；可取消本地广告等待。";
                    SDK.ShowRewardedAd(new RewardedAdRequest("mock-ad-unit"), result => {
                        lastResult = (result.Success ? (result.Completed ? "Mock 完整观看（非发奖凭证）" : "Mock 提前关闭") : DescribeError(result.Error)) + " callId=" + result.CallId;
                        Repaint();
                    }, previewTimeoutSeconds * 1000);
                }
                if (GUILayout.Button("取消本地广告等待")) SDK.CancelRewardedAd();
                if (GUILayout.Button("调用 SDK.Share（仅模拟发起）")) {
                    lastResult = "正在等待 SDK.Share 模拟结果。";
                    SDK.Share(new ShareRequest("Mock share"), result => {
                        lastResult = (result.Success ? "Mock 已发起，非真实分享" : DescribeError(result.Error)) + " callId=" + result.CallId;
                        Repaint();
                    });
                }
                if (GUILayout.Button("调用 SDK.Pay（不产生真实订单）")) {
                    lastResult = "正在等待 SDK.Pay 模拟结果；可取消本地支付等待。";
                    SDK.Pay(new PaymentRequest("mock-session", "mock-product", "mock-purchase"), result => {
                        lastResult = (result.Success ? "Mock 已发货（非真实支付）" : DescribeError(result.Error)) + " callId=" + result.CallId;
                        Repaint();
                    }, previewTimeoutSeconds * 1000);
                }
                if (GUILayout.Button("取消本地支付等待")) SDK.CancelPay();
                if (GUILayout.Button("调用 SDK.Login")) {
                    lastResult = "正在等待 SDK.Login 模拟结果；可取消当前登录。";
                    SDK.Login(result => {
                        lastResult = (result.Success ? "Mock 成功（非真实会话）" : DescribeError(result.Error)) + " callId=" + result.CallId;
                        Repaint();
                    }, previewTimeoutSeconds * 1000);
                }
                if (GUILayout.Button("取消当前登录")) SDK.CancelLogin();
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("内存存储预览（初始化/退出后清空）", EditorStyles.boldLabel);
                storageKey = EditorGUILayout.TextField("Key", storageKey);
                storageValue = EditorGUILayout.TextField("Value", storageValue);
                if (GUILayout.Button("写入 SDK.SetStorageSync")) ShowStorage(SDK.SetStorageSync(storageKey, storageValue));
                if (GUILayout.Button("读取 SDK.GetStorageSync")) {
                    // Release the IMGUI text editor buffer before replacing Value with the stored result.
                    GUI.FocusControl(null);
                    ShowStorage(SDK.GetStorageSync(storageKey));
                }
                if (GUILayout.Button("删除 SDK.RemoveStorageSync")) ShowStorage(SDK.RemoveStorageSync(storageKey));
                if (GUILayout.Button("释放 SDK")) { UnitySdkHost.Shutdown(); lastResult = "已释放"; }
                }
            }
            EditorGUILayout.LabelField(lastResult, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndScrollView();
        }
        static string DescribeError(SdkError error) {
            var reason = error.Reason;
            if (error.Code == "MODULE.DISABLED" && (string.IsNullOrEmpty(reason) || reason == error.Code))
                reason = "该能力未启用；请勾选对应模块后重新初始化。";
            return string.IsNullOrEmpty(reason) || reason == error.Code ? error.Code : error.Code + " / " + reason;
        }
        void ShowStorage(StorageResult result) {
            lastResult = (result.Success ? "存储成功" : DescribeError(result.Error)) + " callId=" + result.CallId;
            if (result.Success && result.Value != null) storageValue = result.Value;
        }
    }
}
