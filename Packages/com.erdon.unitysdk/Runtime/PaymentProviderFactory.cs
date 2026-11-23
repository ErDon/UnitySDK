#if !ERDON_EXCLUDE_PAYMENTS || UNITY_EDITOR
using System;
namespace ErDon.UnitySDK
{
    // Adds a backend-composed payment capability without coupling core to vendor/HTTP packages.
    internal sealed class PaymentProviderFactory : IPlatformSdkProviderFactory, IShareProviderFactory,
        IRewardedAdsProviderFactory, IPayProviderFactory, INetworkProviderFactory, IAnalyticsProviderFactory
    {
        readonly IPlatformSdkProviderFactory platform;
        readonly Func<Action<Action>,IPay> createPayment;
        public string Platform { get { return platform.Platform; } }
        internal PaymentProviderFactory(IPlatformSdkProviderFactory platform,Func<Action<Action>,IPay> createPayment)
        { this.platform=platform;this.createPayment=createPayment; }
        public IAuth CreateAuth(Action<Action> dispatch){return platform.CreateAuth(dispatch);}
        public IStorage CreateStorage(){return platform.CreateStorage();}
        public IShare CreateShare(){var factory=platform as IShareProviderFactory;return factory==null?null:factory.CreateShare();}
        public IRewardedAds CreateRewardedAds(){var factory=platform as IRewardedAdsProviderFactory;return factory==null?null:factory.CreateRewardedAds();}
        public IAnalytics CreateAnalytics(){var factory=platform as IAnalyticsProviderFactory;return factory==null?null:factory.CreateAnalytics();}
        public INetwork CreateNetwork(){var factory=platform as INetworkProviderFactory;return factory==null?null:factory.CreateNetwork();}
        public IPay CreatePay(Action<Action> dispatch){return createPayment(dispatch);}
    }
}
#endif
