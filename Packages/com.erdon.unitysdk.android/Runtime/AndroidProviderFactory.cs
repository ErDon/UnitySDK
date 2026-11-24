using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace ErDon.UnitySDK.Android
{
    /// <summary>Fresh SDK-owned wrappers; native channel login must return a server-verified game session.</summary>
    public sealed class AndroidProviderFactory : IPlatformSdkProviderFactory, IShareProviderFactory, IRewardedAdsProviderFactory, IAnalyticsProviderFactory, IPayProviderFactory
    {
        public string Platform { get { return "Android"; } }
        readonly Func<string,IAndroidBridge> createBridge;
        readonly Func<Action<Action>,IPay> createPayment;
        public AndroidProviderFactory(string gameId,string environment) {
            var context=System.Threading.SynchronizationContext.Current;
            if(context==null)throw new InvalidOperationException("Create Android factory on Unity main thread");
            createBridge=module=>new AndroidBridge(gameId,environment,module,action=>context.Post(_=>action(),null));
        }
        public AndroidProviderFactory(Func<string,IAndroidBridge> createBridge,Func<Action<Action>,IPay> createPayment=null) {
            if(createBridge==null)throw new ArgumentNullException("createBridge");this.createBridge=createBridge;this.createPayment=createPayment;
        }
        IAndroidBridge Create(string module) { var value=createBridge(module);if(value==null)throw new InvalidOperationException("Android bridge factory returned null");return value; }
        public IAuth CreateAuth(Action<Action> dispatch) {
#if ERDON_EXCLUDE_LOGIN && !UNITY_EDITOR
            throw new InvalidOperationException("CONFIG.MODULE_NOT_COMPILED: LOGIN");
#else
            return new Auth(Create("LOGIN"));
#endif
        }
        public IStorage CreateStorage() {
#if ERDON_EXCLUDE_STORAGE && !UNITY_EDITOR
            throw new InvalidOperationException("CONFIG.MODULE_NOT_COMPILED: STORAGE");
#else
            return new Storage(Create("STORAGE"));
#endif
        }
        public IShare CreateShare() {
#if ERDON_EXCLUDE_SHARE && !UNITY_EDITOR
            throw new InvalidOperationException("CONFIG.MODULE_NOT_COMPILED: SHARE");
#else
            return new TextShare(Create("SHARE"));
#endif
        }
        public IRewardedAds CreateRewardedAds() {
#if ERDON_EXCLUDE_ADS && !UNITY_EDITOR
            throw new InvalidOperationException("CONFIG.MODULE_NOT_COMPILED: ADS");
#else
            return new Ads(Create("ADS"));
#endif
        }
        public IAnalytics CreateAnalytics() {
#if ERDON_EXCLUDE_ANALYTICS && !UNITY_EDITOR
            throw new InvalidOperationException("CONFIG.MODULE_NOT_COMPILED: ANALYTICS");
#else
            return new Analytics(Create("ANALYTICS"));
#endif
        }
        public IPay CreatePay(Action<Action> dispatch) {
#if ERDON_EXCLUDE_PAYMENTS && !UNITY_EDITOR
            throw new InvalidOperationException("CONFIG.MODULE_NOT_COMPILED: PAYMENTS");
#else
            if(createPayment==null)throw new InvalidOperationException("CONFIG.PAYMENT_PROVIDER_REQUIRED");
            var value=createPayment(dispatch);if(value==null)throw new InvalidOperationException("Payment factory returned null");return value;
#endif
        }
        abstract class Owned : IDisposable {
            protected readonly IAndroidBridge bridge;protected bool disposed;
            protected Owned(IAndroidBridge bridge){this.bridge=bridge;}
            public void Dispose(){if(disposed)return;disposed=true;bridge.Dispose();}
        }
#if !ERDON_EXCLUDE_LOGIN || UNITY_EDITOR
        sealed class Auth : Owned,IAuth {
            public Auth(IAndroidBridge bridge):base(bridge){}
            public void Login(Action<LoginResult> callback) { bridge.Call("login","{}",r=>callback(!r.success?LoginResult.Fail(r.Error):string.IsNullOrWhiteSpace(r.value)?LoginResult.Fail("AUTH.INVALID_RESPONSE","Missing game session"):LoginResult.Ok(r.value))); }
        }
#endif
#if !ERDON_EXCLUDE_STORAGE || UNITY_EDITOR
        sealed class Storage : Owned,IStorage {
            public Storage(IAndroidBridge bridge):base(bridge){}
            StorageResult Send(string method,string key,string value=null){var r=bridge.Storage(method,key,value);return r.success?StorageResult.Ok(r.value):StorageResult.Fail(r.code,r.reason);}
            public StorageResult Set(string key,string value){return Send("set",key,value);}
            public StorageResult Get(string key){return Send("get",key);}
            public StorageResult Remove(string key){return Send("remove",key);}
        }
#endif
#if !ERDON_EXCLUDE_SHARE || UNITY_EDITOR
        sealed class TextShare : Owned,IShare {
            public TextShare(IAndroidBridge bridge):base(bridge){}
            public void Share(ShareRequest request,Action<ShareResult> callback) {
                if(request==null){callback(ShareResult.Fail("SHARE.INVALID_REQUEST","Share request required"));return;}
                if(!string.IsNullOrEmpty(request.ImageUrl)){callback(ShareResult.Fail("CAPABILITY.NOT_SUPPORTED","Android default share supports text only"));return;}
                bridge.Call("share",JsonUtility.ToJson(new SharePayload {title=request.Title,text=string.IsNullOrEmpty(request.Query)?request.Title:request.Title+"\n"+request.Query}),r=>callback(r.success?ShareResult.Dispatched():ShareResult.Fail(r.Error)));
            }
        }
        [Serializable] sealed class SharePayload {public string title,text;}
#endif
#if !ERDON_EXCLUDE_ADS || UNITY_EDITOR
        sealed class Ads : Owned,IRewardedAds {
            public Ads(IAndroidBridge bridge):base(bridge){}
            public void Show(RewardedAdRequest request,Action<RewardedAdResult> callback) {
                if(request==null){callback(RewardedAdResult.Fail("ADS.INVALID_REQUEST","Ad request required"));return;}
                bridge.Call("ads",JsonUtility.ToJson(new AdPayload{unitId=request.AdUnitId}),r=>callback(r.success?RewardedAdResult.Closed(r.completed):RewardedAdResult.Fail(r.Error)));
            }
        }
        [Serializable] sealed class AdPayload {public string unitId;}
#endif
#if !ERDON_EXCLUDE_ANALYTICS || UNITY_EDITOR
        sealed class Analytics : Owned,IAnalytics,IAsyncAnalytics {
            public Analytics(IAndroidBridge bridge):base(bridge){}
            public AnalyticsResult ReportEvent(AnalyticsEvent value){return AnalyticsResult.Fail("ANALYTICS.ASYNC_REQUIRED","Use SDK.ReportEvent(event, callback) for Android");}
            public IAnalyticsOperation ReportEvent(AnalyticsEvent value,Action<AnalyticsResult> callback) {
                if(value==null)throw new ArgumentNullException("value");
                var json=new StringBuilder("{");bool first=true;
                foreach(var pair in value.Parameters){if(!first)json.Append(',');first=false;json.Append(Quote(pair.Key)).Append(':').Append(pair.Value is string?Quote((string)pair.Value):Convert.ToString(pair.Value,CultureInfo.InvariantCulture));}
                json.Append('}');
                return new AnalyticsOperation(bridge.Call("analytics",JsonUtility.ToJson(new EventPayload{name=value.Name,parametersJson=json.ToString()}),r=>callback(r.success?AnalyticsResult.DispatchAccepted():AnalyticsResult.Fail(r.Error))));
            }
            static string Quote(string value){var json=JsonUtility.ToJson(new StringValue{value=value});return json.Substring(9,json.Length-10);}
        }
        sealed class AnalyticsOperation : IAnalyticsOperation { Action cancel;public AnalyticsOperation(Action cancel){this.cancel=cancel;}public void Cancel(){var action=cancel;cancel=null;if(action!=null)action();} }
        [Serializable] sealed class EventPayload {public string name,parametersJson;}
        [Serializable] sealed class StringValue {public string value;}
#endif
    }
}
