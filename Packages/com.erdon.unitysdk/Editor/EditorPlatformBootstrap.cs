using System;
using UnityEditor;
namespace ErDon.UnitySDK.Editor
{
    [InitializeOnLoad]
    public static class EditorPlatformBootstrap
    {
        // Application-owned constructor only; never retain an exchange instance in the editor.
        public static Func<ISessionExchange> SessionExchangeFactory { get; set; }
        public static Func<INetwork> NetworkFactory { get; set; }
        public static Func<Action<Action>,IPay> PaymentFactory { get; set; }
        static EditorPlatformBootstrap()
        {
            EditorApplication.playModeStateChanged += state => {
                if(state == PlayModeStateChange.ExitingPlayMode) { SessionExchangeFactory = null; NetworkFactory = null; PaymentFactory = null; }
            };
        }
        public static void Begin(Action<StartupResult> callback, Action<SdkLogLevel,string> logCallback = null)
        {
            if(!EditorApplication.isPlaying)throw new InvalidOperationException("CONFIG.PLAY_MODE_REQUIRED");
            if(callback==null)throw new ArgumentNullException("callback");
            var settings=SdkProjectSettings.instance;
            if(settings.platform==DistributionPlatform.Mock)throw new InvalidOperationException("CONFIG.REAL_PLATFORM_REQUIRED");
            if(PlatformDependencyCatalog.CheckVendorInstallations(settings.platform.ToString()).Count!=0)
                throw new InvalidOperationException("CONFIG.FOREIGN_VENDOR_INSTALLED");
            UnitySdkHost.BeginConfiguredStartup(settings.CreateRuntimeConfiguration(),SessionExchangeFactory,callback,logCallback,createNetwork:NetworkFactory,createPayment:PaymentFactory);
        }
    }
}
