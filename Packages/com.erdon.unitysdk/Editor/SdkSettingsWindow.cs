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
            if (rewardedAds || payments || share || storage || network || analytics)
                issues.Add("已勾选尚未实现的能力。当前仅提供 Editor Mock 登录示例。");
            return issues;
        }
    }
    public sealed class SdkSettingsWindow : EditorWindow
    {
        Vector2 scroll;
        [MenuItem("Tools/ErDon SDK/配置与预览")]
        public static void Open() { GetWindow<SdkSettingsWindow>("SDK 配置"); }
        void OnGUI()
        {
            var settings = SdkProjectSettings.instance;
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.HelpBox("开发预览版：配置会保存，但尚未连接真实平台装配。当前可运行的示例仅为 Editor Mock 登录。", MessageType.Info);
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
            settings.storage = EditorGUILayout.Toggle("存储（待实现）", settings.storage);
            settings.network = EditorGUILayout.Toggle("网络（待实现）", settings.network);
            settings.analytics = EditorGUILayout.Toggle("数据上报（待实现）", settings.analytics);
            if (EditorGUI.EndChangeCheck()) settings.SaveSettings();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("配置预览", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("核心包", "com.erdon.unitysdk 0.1.0");
            EditorGUILayout.LabelField("平台实现", settings.platform == DistributionPlatform.Mock ? "Editor 示例；非真实服务" : "未安装 / 未实现");
            EditorGUILayout.LabelField("登录开关", settings.login ? "计划启用" : "计划关闭");
            EditorGUILayout.HelpBox("没有引入微信、抖音、TikTok 或 Android/iOS 官方包。此预览不是构建产物裁剪证明。", MessageType.Info);
            foreach (var issue in settings.Validate()) EditorGUILayout.HelpBox(issue, MessageType.Error);
            if (GUILayout.Button("检查并保存配置")) {
                settings.SaveSettings();
                var issues = settings.Validate();
                EditorUtility.DisplayDialog("SDK 配置检查", issues.Count == 0 ? "配置检查通过；仅适用于当前 Editor 示例范围。" : string.Join("\n", issues), "确定");
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
