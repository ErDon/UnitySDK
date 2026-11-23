using System;
using System.Collections.Generic;
namespace ErDon.UnitySDK
{
    // Registrations contain constructors only. No vendor object is created until configuration passes.
    public static class SdkPlatformRegistry
    {
        sealed class Binding
        {
            public Func<IPlatformRuntime> Runtime;
            public Func<string,string,Func<ISessionExchange>,IPlatformSdkProviderFactory> Factory;
            public bool Payments;
            public bool RequiresPaymentFactory;
        }
        static readonly Dictionary<string,Binding> bindings = new Dictionary<string,Binding>(StringComparer.Ordinal);
        public static void Register(string platform, Func<IPlatformRuntime> runtime,
            Func<string,string,Func<ISessionExchange>,IPlatformSdkProviderFactory> factory, bool supportsPayments = false, bool requiresPaymentFactory = false)
        {
            SDK.CheckHostLifecycleEntry();
            if(string.IsNullOrWhiteSpace(platform)||runtime==null||factory==null)throw new ArgumentException("Platform constructors are required");
            if(bindings.ContainsKey(platform))throw new InvalidOperationException("CONFIG.DUPLICATE_PLATFORM");
            if(requiresPaymentFactory&&!supportsPayments)throw new ArgumentException("Payment factory requirement needs payment support");
            bindings.Add(platform,new Binding{Runtime=runtime,Factory=factory,Payments=supportsPayments,RequiresPaymentFactory=requiresPaymentFactory});
        }
        internal static void Reset(){bindings.Clear();}
        public static SdkStartup CreateStartup(SdkRuntimeConfiguration configuration, Func<ISessionExchange> createSessionExchange,
            Action<Action> dispatch, Action<SdkLogLevel,string> engineLog, Action<StartupResult> callback,
            Action<SdkLogLevel,string> logCallback = null, int timeoutMilliseconds = 15000, Func<INetwork> createNetwork = null,
            Func<Action<Action>,IPay> createPayment = null)
        {
            SDK.CheckHostLifecycleEntry();
            if(configuration==null||dispatch==null||engineLog==null||callback==null)throw new ArgumentNullException();
            if(timeoutMilliseconds<=0)throw new ArgumentOutOfRangeException("timeoutMilliseconds");
            var config=configuration.ToSdkConfig(logCallback);
            string gameId=configuration.gameId, environment=configuration.Environment;
            Binding binding;
            if(!bindings.TryGetValue(config.Platform,out binding))throw new InvalidOperationException("CONFIG.PLATFORM_NOT_REGISTERED");
            if(config.PayEnabled&&!binding.Payments)throw new InvalidOperationException("CONFIG.PAYMENT_UNIMPLEMENTED");
            if(config.PayEnabled&&binding.RequiresPaymentFactory&&createPayment==null)throw new InvalidOperationException("CONFIG.PAYMENT_PROVIDER_REQUIRED");
            if(config.LoginEnabled&&createSessionExchange==null)throw new InvalidOperationException("CONFIG.SESSION_EXCHANGE_REQUIRED");
            if(config.NetworkEnabled&&createNetwork==null)throw new InvalidOperationException("CONFIG.NETWORK_PROVIDER_REQUIRED");
            // Disabled login cannot accidentally construct an exchange via the factory.
            var factory=binding.Factory(gameId,environment,config.LoginEnabled?createSessionExchange:null);
            if(factory==null||factory.Platform!=config.Platform)throw new InvalidOperationException("CONFIG.PLATFORM_MISMATCH");
#if !ERDON_EXCLUDE_NETWORK || UNITY_EDITOR
            if(config.NetworkEnabled)factory=new NetworkProviderFactory(factory,createNetwork);
#endif
#if !ERDON_EXCLUDE_PAYMENTS || UNITY_EDITOR
            if(config.PayEnabled&&createPayment!=null)factory=new PaymentProviderFactory(factory,createPayment);
#endif
            var runtime=binding.Runtime();
            if(runtime==null)throw new InvalidOperationException("CONFIG.RUNTIME_REQUIRED");
            try {return new SdkStartup(config,runtime,factory,dispatch,engineLog,callback,timeoutMilliseconds);}
            catch {try{runtime.Dispose();}catch{}throw;}
        }
    }
}
