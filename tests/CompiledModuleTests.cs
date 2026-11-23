using System;
using ErDon.UnitySDK;
using ErDon.UnitySDK.TikTok;
class CompiledModuleTests {
 static void Main() {
  var configs=new[]{new SdkConfig{LoginEnabled=true},new SdkConfig{LoginEnabled=false,StorageEnabled=true},new SdkConfig{LoginEnabled=false,ShareEnabled=true},new SdkConfig{LoginEnabled=false,RewardedAdsEnabled=true},new SdkConfig{LoginEnabled=false,AnalyticsEnabled=true},new SdkConfig{LoginEnabled=false,NetworkEnabled=true},new SdkConfig{LoginEnabled=false,PayEnabled=true}};
  foreach(var c in configs){bool rejected=false;try{SdkCompiledModules.Validate(c);}catch(InvalidOperationException e){rejected=e.Message.StartsWith("CONFIG.MODULE_NOT_COMPILED:");}if(!rejected)throw new Exception("Compiled capability was reopened");}
  SdkCompiledModules.Validate(new SdkConfig{LoginEnabled=false});
  var f=new TikTokProviderFactory("test","test");
  Action[] attempts={()=>f.CreateAuth(a=>a()),()=>f.CreateStorage(),()=>f.CreateShare(),()=>f.CreateRewardedAds(),()=>f.CreateAnalytics()};
  foreach(var call in attempts){bool rejected=false;try{call();}catch(InvalidOperationException e){rejected=e.Message.StartsWith("CONFIG.MODULE_NOT_COMPILED:");}if(!rejected)throw new Exception("Factory did not reject excluded capability");}
  foreach(var name in new[]{"CredentialProvider","Storage","Share","RewardedAds","Analytics"})
   if(typeof(TikTokProviderFactory).Assembly.GetType("ErDon.UnitySDK.TikTok.TikTok"+name)!=null)throw new Exception("Excluded type remains: "+name);
  foreach(var name in new[]{"VerifiedPayment","NetworkProviderFactory"})
   if(typeof(SDK).Assembly.GetType("ErDon.UnitySDK."+name)!=null)throw new Exception("Excluded core implementation remains: "+name);
  Console.WriteLine("COMPILED_MODULE_TESTS_PASS");
 }
}
