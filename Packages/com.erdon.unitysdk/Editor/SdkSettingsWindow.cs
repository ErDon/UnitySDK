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
        public List<string> Validate()
        {
            var issues = new List<string>();
            if (string.IsNullOrWhiteSpace(gameId)) issues.Add("缺少游戏标识 Game ID。");
            if (production && platform == DistributionPlatform.Mock) issues.Add("生产环境禁止 Mock。");
            if (platform != DistributionPlatform.Mock) {
                issues.Add("目标平台适配包尚未实现；当前不能生成真实平台接入。");
                if (string.IsNullOrWhiteSpace(appId)) issues.Add("缺少平台应用标识 App ID。");
            }
            if (rewardedAds || payments || share || network || analytics)
                issues.Add("已勾选尚未实现的能力。当前提供 Editor Mock 登录和存储示例。");
            return issues;
        }
    }
    public sealed class SdkSettingsWindow : EditorWindow
    {
        Vector2 scroll;
        MockLoginOutcome outcome;
        string lastResult = "尚未调用";
        string storageKey = "preview.key", storageValue = "preview-value";
        [MenuItem("Tools/ErDon SDK/配置与预览")]
        public static void Open() { GetWindow<SdkSettingsWindow>("SDK 配置"); }
        void OnGUI()
        {
            var settings = SdkProjectSettings.instance;
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.HelpBox("开发预览版：配置已连接 Editor Mock 初始化；真实平台装配尚未实现。", MessageType.Info);
            EditorGUI.BeginChangeCheck();
            settings.gameId = EditorGUILayout.TextField("Game ID", settings.gameId);
            settings.platform = (DistributionPlatform)EditorGUILayout.EnumPopup("发行平台", settings.platform);
            settings.production = EditorGUILayout.Toggle("生产环境", settings.production);
            settings.appId = EditorGUILayout.TextField("App ID（非密钥）", settings.appId);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("能力模块", EditorStyles.boldLabel);
            settings.login = EditorGUILayout.Toggle("登录", settings.login);
            settings.rewardedAds = EditorGUILayout.Toggle("激励广告（待实现）", settings.rewardedAds);
            settings.payments = EditorGUILayout.Toggle("支付（待实现）", settings.payments);
            settings.share = EditorGUILayout.Toggle("分享（待实现）", settings.share);
            settings.storage = EditorGUILayout.Toggle("存储（Editor Mock）", settings.storage);
            settings.network = EditorGUILayout.Toggle("网络（待实现）", settings.network);
            settings.analytics = EditorGUILayout.Toggle("数据上报（待实现）", settings.analytics);
            if (EditorGUI.EndChangeCheck()) settings.SaveSettings();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("配置预览", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("核心包", "com.erdon.unitysdk 0.1.0");
            EditorGUILayout.LabelField("平台实现", settings.platform == DistributionPlatform.Mock ? "Editor 示例；非真实服务" : "未安装 / 未实现");
            EditorGUILayout.LabelField("登录开关", settings.login ? "Mock 初始化时启用" : "Mock 初始化时关闭");
            EditorGUILayout.HelpBox("没有引入微信、抖音、TikTok 或 Android/iOS 官方包。此预览不是构建产物裁剪证明。", MessageType.Info);
            foreach (var issue in settings.Validate()) EditorGUILayout.HelpBox(issue, MessageType.Error);
            if (GUILayout.Button("检查并保存配置")) {
                settings.SaveSettings();
                var issues = settings.Validate();
                EditorUtility.DisplayDialog("SDK 配置检查", issues.Count == 0 ? "配置检查通过；仅适用于当前 Editor 示例范围。" : string.Join("\n", issues), "确定");
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Mock 调试（仅 Play Mode）", EditorStyles.boldLabel);
            outcome = (MockLoginOutcome)EditorGUILayout.EnumPopup("模拟结果", outcome);
            EditorGUILayout.HelpBox("每次初始化读取当前配置；修改模块开关后重新初始化。超时模拟使用 1 秒；其他调用使用默认超时。", MessageType.Info);
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || settings.Validate().Count != 0)) {
                if (GUILayout.Button("按配置初始化 Mock")) {
                    try { EditorSdkBootstrap.Initialize(outcome); lastResult = "Mock 已初始化"; }
                    catch (Exception ex) { lastResult = ex.Message; }
                }
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
                if (GUILayout.Button("释放 SDK")) { SDK.Shutdown(); lastResult = "已释放"; }
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
