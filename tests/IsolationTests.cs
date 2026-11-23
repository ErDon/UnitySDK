using System;
using ErDon.UnitySDK.Editor;
class IsolationTests
{
 static void Require(bool pass,string name){if(!pass)throw new Exception(name);}
 static void Main(){
  var wx=new PlatformDependency{Platform="WeChat",Package="wx",Version="1.0.0",Assemblies=new[]{"WX.Runtime"},AssetRoots=new[]{"Packages/wx/Runtime"}};
  var tt=new PlatformDependency{Platform="TikTok",Package="tt",Version="1.0.0",Assemblies=new[]{"TT.Runtime"},AssetRoots=new[]{"Assets/TikTok"}};
  var deps=new[]{wx,tt};
  var target=new PlatformDependency{Platform="WeChat",Package="com.erdon.unitysdk.wechat",Version="0.1.0",
      Assemblies=new[]{"ErDon.UnitySDK.WeChat"},AssetRoots=new[]{"Packages/com.erdon.unitysdk.wechat"}};
  Require(PlatformIsolationPolicy.CheckTargetAdapter("WeChat",new[]{target},new[]{"ErDon.UnitySDK.WeChat"}).Count==0,"target manifest and Player assembly present");
  Require(PlatformIsolationPolicy.CheckTargetAdapter("WeChat",new PlatformDependency[0],new[]{"ErDon.UnitySDK.WeChat"}).Count>0,"assembly alone insufficient");
  Require(PlatformIsolationPolicy.CheckTargetAdapter("WeChat",new[]{target},new string[0]).Count>0,"installed but excluded adapter rejected");
  Require(PlatformIsolationPolicy.CheckTargetAdapter("WeChat",new[]{target,target},new[]{"ErDon.UnitySDK.WeChat"}).Count>0,"duplicate target manifests rejected");
  Require(PlatformIsolationPolicy.CheckTargetAdapter("TikTok",new[]{target},new[]{"ErDon.UnitySDK.WeChat"}).Count>0,"foreign adapter cannot satisfy target");
  Require(PlatformIsolationPolicy.CheckTargetAdapter("Mock",new[]{target},new[]{"ErDon.UnitySDK.WeChat"}).Count>0,"Mock has no Player adapter");
  target.Assemblies=new[]{"Other"};
  Require(PlatformIsolationPolicy.CheckTargetAdapter("WeChat",new[]{target},new[]{"ErDon.UnitySDK.WeChat"}).Count>0,"manifest assembly declaration required");

  Require(PlatformIsolationPolicy.CheckVendorInstallations("WeChat",new[]{"Assets/Plugins/com.tiktok.minigame"}).Count==1,"vendor without adapter rejected");
  Require(PlatformIsolationPolicy.CheckVendorInstallations("WeChat",new[]{"Packages/com.qq.weixin.minigame"}).Count==0,"target vendor allowed");
  Require(PlatformIsolationPolicy.CheckVendorInstallations("TikTok",new[]{"Assets/Plugins/ByteGame/com.bytedance.starksdk"}).Count==1,"Douyin vendor isolated from TikTok");
  Require(PlatformIsolationPolicy.CheckVendorInstallations("Mock",new[]{"Packages/com.qq.weixin.minigame"}).Count==1,"mock cannot ship real vendor");
  Require(PlatformIsolationPolicy.CheckVendorInstallations("WeChat",new[]{"Assets/Plugins/com.tiktok.minigameOther"}).Count==0,"exact root match");
  Require(PlatformIsolationPolicy.CheckPlatformDefines("WeChat","DEBUG;ERDON_PLATFORM_WECHAT","WebGL").Count==0,"single correct define");
  Require(PlatformIsolationPolicy.CheckPlatformDefines("WeChat","ERDON_PLATFORM_WECHAT;ERDON_PLATFORM_TIKTOK","WebGL").Count>0,"two platforms rejected");
  Require(PlatformIsolationPolicy.CheckPlatformDefines("TikTok","ERDON_PLATFORM_DOUYIN","WebGL").Count>0,"wrong target rejected");
  Require(PlatformIsolationPolicy.CheckPlatformDefines("WeChat","ERDON_PLATFORM_WECHAT;ERDON_PLATFORM_WECAHT","WebGL").Count>0,"unknown platform macro rejected");
  Require(PlatformIsolationPolicy.CheckPlatformDefines("WeChat","ERDON_PLATFORM_WECHAT","Android").Count>0,"native target rejected");
  Require(PlatformIsolationPolicy.CheckPlatformDefines("Mock","DEBUG","StandaloneWindows64").Count==0,"mock without platform define");
  Require(PlatformIsolationPolicy.CheckPlatformDefines("Mock","ERDON_PLATFORM_WECHAT","WebGL").Count>0,"mock with official define rejected");
  Require(PlatformIsolationPolicy.CheckPlatformDefines("AndroidChannel","","Android").Count>0,"unimplemented native target rejected");
  Require(PlatformIsolationPolicy.CheckIncluded("WeChat",deps,new[]{"WX.Runtime.dll"},new[]{"Packages/wx/Runtime/a.js"}).Count==0,"target allowed");
  Require(PlatformIsolationPolicy.CheckIncluded("WeChat",deps,new[]{"TT.Runtime.dll"},new string[0]).Count==1,"foreign DLL rejected");
  Require(PlatformIsolationPolicy.CheckIncluded("WeChat",deps,new[]{"C:\\vendor\\TT.Runtime.dll"},new string[0]).Count==1,"absolute precompiled reference rejected");
  Require(PlatformIsolationPolicy.CheckIncluded("WeChat",deps,new[]{"/vendor/tt.runtime.DLL"},new string[0]).Count==1,"cross platform reference casing rejected");
  Require(PlatformIsolationPolicy.CheckIncluded("WeChat",deps,new string[0],new[]{"Assets\\TikTok\\bridge.jslib"}).Count==1,"foreign bridge rejected");
  Require(PlatformIsolationPolicy.CheckIncluded("WeChat",deps,new string[0],new[]{"Assets/TikTokOther/a"}).Count==0,"segment boundary");
  tt.AssetRoots=new[]{"Assets/../secret"};Require(PlatformIsolationPolicy.ValidateManifest(tt).Count>0,"traversal rejected");
  tt.AssetRoots=new[]{"Assets/TikTok"};tt.Version="latest";Require(PlatformIsolationPolicy.ValidateManifest(tt).Count>0,"floating version rejected");
  Console.WriteLine("PASS: platform isolation manifest and included-artifact policy");
 }
}
