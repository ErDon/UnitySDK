using System;
using System.Collections.Generic;
namespace ErDon.UnitySDK
{
    /// <summary>Portable configuration data. Contains public application IDs, never platform secrets.</summary>
    [Serializable]
    public sealed class SdkRuntimeConfiguration
    {
        public int schemaVersion = 1;
        public string platform = "Mock", gameId = "", appId = "";
        public bool production, login = true, storage, share, payments, rewardedAds, network, analytics;
        public string Environment { get { return production ? "production" : "test"; } }
        public List<string> Validate()
        {
            var issues = new List<string>();
            if (schemaVersion != 1) issues.Add("CONFIG.SCHEMA_UNSUPPORTED");
            if (string.IsNullOrWhiteSpace(gameId)) issues.Add("CONFIG.GAME_ID_REQUIRED");
            if (platform != "Mock" && platform != "WeChat" && platform != "Douyin" && platform != "TikTok") issues.Add("CONFIG.PLATFORM_UNSUPPORTED");
            if (production && platform == "Mock") issues.Add("CONFIG.PRODUCTION_MOCK");
            if (platform != "Mock" && string.IsNullOrWhiteSpace(appId)) issues.Add("CONFIG.APP_ID_REQUIRED");
            if (analytics && platform != "WeChat") issues.Add("CONFIG.CAPABILITY_UNIMPLEMENTED");
            return issues;
        }
        public SdkConfig ToSdkConfig(Action<SdkLogLevel,string> logCallback = null)
        {
            var issues = Validate();
            if (issues.Count != 0) throw new InvalidOperationException(string.Join(",", issues.ToArray()));
            return new SdkConfig { Platform = platform, LoginEnabled = login, StorageEnabled = storage,
                ShareEnabled = share, PayEnabled = payments, RewardedAdsEnabled = rewardedAds, NetworkEnabled = network, AnalyticsEnabled = analytics, LogCallback = logCallback };
        }
    }
}
