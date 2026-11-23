using System;
using ErDon.UnitySDK;
class RuntimeConfigurationTests {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Main(){
  var data=new SdkRuntimeConfiguration{gameId="game",appId="app",platform="WeChat",login=false,storage=true,share=true};
  var config=data.ToSdkConfig();
  Check(!config.LoginEnabled&&config.StorageEnabled&&config.ShareEnabled&&!config.PayEnabled&&config.Platform=="WeChat","module mapping");
  data.storage=false;Check(config.StorageEnabled,"snapshot independence");
  Check(data.Environment=="test","test environment");data.production=true;Check(data.Environment=="production","production environment");
  data.platform="Mock";Check(data.Validate().Contains("CONFIG.PRODUCTION_MOCK"),"production mock rejected");
  data.platform="WeChat";data.schemaVersion=2;Check(data.Validate().Contains("CONFIG.SCHEMA_UNSUPPORTED"),"future schema rejected");
  data.schemaVersion=1;data.network=true;
  bool rejected=false;try{data.ToSdkConfig();}catch(InvalidOperationException){rejected=true;}Check(rejected,"unimplemented not silently dropped");
  data.network=false;data.platform="AndroidChannel";Check(data.Validate().Contains("CONFIG.PLATFORM_UNSUPPORTED"),"native channel remains extension only");
  data.platform="Douyin";data.appId=" ";Check(data.Validate().Contains("CONFIG.APP_ID_REQUIRED"),"app required");
  data.appId="app";data.rewardedAds=true;
  Check(data.ToSdkConfig().RewardedAdsEnabled,"Douyin ad mapping");
  data.platform="Mock";data.production=false;Check(data.ToSdkConfig().RewardedAdsEnabled,"Mock ad mapping");
  data.platform="WeChat";Check(data.ToSdkConfig().RewardedAdsEnabled,"WeChat ad mapping");
  data.platform="TikTok";Check(data.ToSdkConfig().RewardedAdsEnabled,"TikTok ad mapping");
  data.platform="Mock";data.network=true;Check(data.ToSdkConfig().NetworkEnabled,"Mock network mapping");
  Console.WriteLine("PASS: portable configuration mapping, snapshot independence and unsupported configuration rejection");
 }
}
