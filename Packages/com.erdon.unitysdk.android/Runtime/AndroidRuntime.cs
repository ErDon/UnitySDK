using System;
using UnityEngine;
[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]
namespace ErDon.UnitySDK.Android
{
    public sealed class AndroidRuntime : IPlatformRuntime
    {
        public string Platform {get{return "Android";}}
        public bool IsReady {get;private set;}
        bool disposed;
        public void Initialize(Action<SdkError> callback) {
            if(callback==null)throw new ArgumentNullException("callback");
            if(disposed){callback(new SdkError("SDK.DISPOSED","Android runtime disposed"));return;}
#if UNITY_ANDROID && !UNITY_EDITOR
            try {
                using(var bridge=new AndroidJavaClass("com.erdon.sdk.android.UnityBridge")) {
                    if(bridge.CallStatic<int>("protocolVersion")!=1){callback(new SdkError("BRIDGE.VERSION_MISMATCH","Update Android AAR and Unity bridge together"));return;}
                }
                IsReady=true;callback(null);
            } catch {callback(new SdkError("INIT.ANDROID_BRIDGE_MISSING","Android AAR unavailable"));}
#else
            callback(new SdkError("PLATFORM.UNAVAILABLE","Android runtime requires an Android device; use Mock in Editor"));
#endif
        }
        public void Dispose(){disposed=true;IsReady=false;}
    }
    static class AndroidRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register(){SdkPlatformRegistry.Register("Android",()=>new AndroidRuntime(),
            (game,environment,exchange)=>new AndroidProviderFactory(game,environment),
            supportsPayments:true,requiresPaymentFactory:true,requiresSessionExchange:false);}
    }
}
