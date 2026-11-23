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
