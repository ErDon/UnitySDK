using System;
using System.Collections.Generic;
using UnityEditor;
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
        public void ApplyCapabilityDefines()
        {
            var group = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            if (group != BuildTargetGroup.WebGL) throw new InvalidOperationException("Switch to WebGL before applying capability selection");
            var defines = new List<string>(PlayerSettings.GetScriptingDefineSymbolsForGroup(group).Split(';'));
            string[] symbols = { "ERDON_EXCLUDE_LOGIN", "ERDON_EXCLUDE_STORAGE", "ERDON_EXCLUDE_SHARE", "ERDON_EXCLUDE_ADS", "ERDON_EXCLUDE_ANALYTICS" };
            bool[] enabled = { login, storage, share, rewardedAds, analytics };
            for (int i = 0; i < symbols.Length; i++) {
                defines.RemoveAll(x => x == symbols[i] || string.IsNullOrWhiteSpace(x));
                if (!enabled[i]) defines.Add(symbols[i]);
            }
            SaveSettings();
            PlayerSettings.SetScriptingDefineSymbolsForGroup(group, string.Join(";", defines.ToArray()));
        }
        public List<string> ValidateCapabilityDefines(string symbols)
        {
            var issues = new List<string>();
            var defined = new HashSet<string>((symbols ?? "").Split(';'));
            string[] names = { "LOGIN", "STORAGE", "SHARE", "ADS", "ANALYTICS" };
            bool[] enabled = { login, storage, share, rewardedAds, analytics };
            for (int i = 0; i < names.Length; i++)
                if (enabled[i] && defined.Contains("ERDON_EXCLUDE_" + names[i]))
                    issues.Add("CONFIG.MODULE_NOT_COMPILED: " + names[i] + "；请重新应用构建能力选择。");
            return issues;
        }
        public void SaveSettings() { Save(true); }
        public SdkRuntimeConfiguration CreateRuntimeConfiguration()
        {
            return new SdkRuntimeConfiguration { platform = platform.ToString(), production = production,
                gameId = gameId, appId = appId, login = login, storage = storage, share = share,
                payments = payments, rewardedAds = rewardedAds, network = network, analytics = analytics };
        }
        public List<string> Validate(bool developmentBuild = false)
        {
            var issues = new List<string>();
            if (string.IsNullOrWhiteSpace(gameId)) issues.Add("缺少游戏标识 Game ID。");
            if (production && platform == DistributionPlatform.Mock) issues.Add("生产环境禁止 Mock。");
            if (platform != DistributionPlatform.Mock) {
                if (!developmentBuild || production)
                    issues.Add("正式构建验收尚未完成；仅允许非生产配置的 Development Build 进行技术验证。");
                if (string.IsNullOrWhiteSpace(appId)) issues.Add("缺少平台应用标识 App ID。");
            }
            foreach (var issue in CreateRuntimeConfiguration().Validate())
                if (!issues.Contains(issue)) issues.Add(issue);
            return issues;
        }
    }
}
