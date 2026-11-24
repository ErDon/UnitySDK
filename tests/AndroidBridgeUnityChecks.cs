using System;
using System.Collections.Generic;
using System.Reflection;
using ErDon.UnitySDK;
using ErDon.UnitySDK.Android;
using ErDon.UnitySDK.Editor;
using UnityEditor;
using UnityEngine;

// Run in an isolated Unity project with core + android packages and ERDON_PLATFORM_ANDROID.
public static class AndroidBridgeUnityChecks
{
    sealed class Bridge : IAndroidBridge {
        public int Calls,Disposed;
        public string Method,Payload;
        public AndroidReply Reply=new AndroidReply{schemaVersion=1,success=true,value="verified-session",completed=true};
        public AndroidReply Storage(string operation,string key,string value){Calls++;return Reply;}
        public Action Call(string method,string payload,Action<AndroidReply> callback){Calls++;Method=method;Payload=payload;callback(Reply);return ()=>{};}
        public void Dispose(){Disposed++;}
    }
    sealed class Runtime : IPlatformRuntime {
        public string Platform{get{return "Android";}}public bool IsReady{get;private set;}
        public void Initialize(Action<SdkError> callback){IsReady=true;callback(null);}public void Dispose(){IsReady=false;}
    }
    [Serializable] sealed class ShareData {public string title,text;}
    [Serializable] sealed class EventData {public string name,parametersJson;}
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    public static void Run() {
        try {
            var bridges=new Dictionary<string,Bridge>();
            var factory=new AndroidProviderFactory(module=>{var b=new Bridge();bridges.Add(module,b);return b;});
            SDK.InitializeWithFactory(new SdkConfig{Platform="Android",LoginEnabled=false,StorageEnabled=true},factory,a=>a(),(l,s)=>{});
            Check(bridges.Count==1&&bridges.ContainsKey("STORAGE"),"disabled capability constructed");
            Check(SDK.GetStorageSync("key").Value=="verified-session","aggregate storage dispatch");
            SDK.Shutdown();Check(bridges["STORAGE"].Disposed==1,"owned storage disposal");bridges.Clear();
            var auth=factory.CreateAuth(a=>a());LoginResult login=null;auth.Login(r=>login=r);Check(login.Success&&login.Session=="verified-session","native verified login");auth.Dispose();
            var share=factory.CreateShare();ShareResult shared=null;
            share.Share(new ShareRequest("title","https://example.com/image.png"),r=>shared=r);
            Check(shared.Error.Code=="CAPABILITY.NOT_SUPPORTED"&&bridges["SHARE"].Calls==0,"image share must not silently become text");
            share.Share(new ShareRequest("中文\"title",query:"a=1"),r=>shared=r);
            Check(shared.Success&&JsonUtility.FromJson<ShareData>(bridges["SHARE"].Payload).text=="中文\"title\na=1","share JSON escaping");share.Dispose();
            var ads=factory.CreateRewardedAds();RewardedAdResult ad=null;
            bridges["ADS"].Reply.completed=false;ads.Show(new RewardedAdRequest("unit"),r=>ad=r);
            Check(ad.Success&&!ad.Completed,"early ad close is not completion");ads.Dispose();
            var analytics=factory.CreateAnalytics();Check(analytics.ReportEvent(new AnalyticsEvent("event")).Error.Code=="ANALYTICS.ASYNC_REQUIRED","sync reporting must not fake success");
            AnalyticsResult reported=null;
            ((IAsyncAnalytics)analytics).ReportEvent(new AnalyticsEvent("event",new Dictionary<string,object>{{"text","x\"\ny"},{"number",1.25}}),r=>reported=r);
            var eventData=JsonUtility.FromJson<EventData>(bridges["ANALYTICS"].Payload);
            Check(reported.Dispatched&&eventData.parametersJson=="{\"text\":\"x\\\"\\ny\",\"number\":1.25}","analytics JSON types/escaping");analytics.Dispose();
            var paymentBridge=new Bridge();var gateway=new AndroidPaymentGateway(paymentBridge);PaymentLaunchResult payment=null;
            gateway.Launch(new AndroidPaymentParameters{orderId="order",parametersJson="{\"signed\":true}"},r=>payment=r);
            Check(payment.Error==null&&payment.RequiresServerQuery,"cashier must still require server confirmation");gateway.Dispose();
            Check(AndroidReply.Parse("{}").Error.Code=="BRIDGE.INVALID_RESPONSE","malformed envelope");
            Check(AndroidReply.Parse("{\"schemaVersion\":2,\"success\":true}").Error!=null,"protocol mismatch");
            var queue=new List<Action>();AndroidReply dispatched=null;
            using(var editorBridge=new AndroidBridge("game","test","LOGIN",a=>queue.Add(a))) {
                editorBridge.Call("login","{}",r=>dispatched=r);
                Check(dispatched==null&&queue.Count==1,"bridge bypassed configured dispatch queue");
                queue[0]();queue.Clear();Check(dispatched.code=="PLATFORM.UNAVAILABLE","Editor native call must not fake success");
                editorBridge.Call("login","{}",r=>{throw new Exception("late result survived bridge close");});
            }
            foreach(var next in queue)next();
            var config=new SdkRuntimeConfiguration{platform="Android",gameId="game",appId="com.example.game",login=true};
            Check(config.Validate().Count==0,"Android config rejected");
            typeof(SdkPlatformRegistry).GetMethod("Reset",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
            SdkPlatformRegistry.Register("Android",()=>new Runtime(),(g,e,x)=>new AndroidProviderFactory(m=>new Bridge()),true,true,false);
            StartupResult startupResult=null;
            using(var startup=SdkPlatformRegistry.CreateStartup(config,null,a=>a(),(l,s)=>{},r=>startupResult=r)){startup.Start();startup.Tick();Check(startupResult.Success,"native login must not require mini-game exchange");}
            SDK.Shutdown();config.payments=true;bool rejected=false;
            try{SdkPlatformRegistry.CreateStartup(config,null,a=>a(),(l,s)=>{},r=>{});}catch(InvalidOperationException e){rejected=e.Message=="CONFIG.PAYMENT_PROVIDER_REQUIRED";}
            Check(rejected,"payment factory requirement lost");
            Check(PlatformIsolationPolicy.CheckPlatformDefines("Android","ERDON_PLATFORM_ANDROID","Android").Count==0,"Android target define");
            Check(PlatformIsolationPolicy.CheckPlatformDefines("Android","ERDON_PLATFORM_ANDROID;ERDON_PLATFORM_TIKTOK","Android").Count!=0,"mixed platform allowed");
            Check(PlatformIsolationPolicy.CheckPlatformDefines("Android","ERDON_PLATFORM_ANDROID","WebGL").Count!=0,"wrong engine target allowed");
            var importer=AssetImporter.GetAtPath("Packages/com.erdon.unitysdk.android/Runtime/Plugins/Android/erdon-android.aar") as PluginImporter;
            Check(importer!=null&&!importer.GetCompatibleWithAnyPlatform()&&!importer.GetCompatibleWithEditor()&&importer.GetCompatibleWithPlatform(BuildTarget.Android)&&!importer.GetCompatibleWithPlatform(BuildTarget.WebGL),"AAR importer isolation");
            Debug.Log("ANDROID_UNITY_CHECKS_PASS: configuration, factory ownership, bridge payloads, payment boundary, platform and AAR isolation");
            EditorApplication.Exit(0);
        }catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
    }
}
