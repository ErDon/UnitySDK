using System;
namespace ErDon.UnitySDK
{
    // Adds an optional transport without making platform packages depend on the HTTP package.
    internal sealed class NetworkProviderFactory : IPlatformSdkProviderFactory, IShareProviderFactory,
        IRewardedAdsProviderFactory, IPayProviderFactory, INetworkProviderFactory
    {
        readonly IPlatformSdkProviderFactory platform;
        readonly Func<INetwork> createNetwork;
        public string Platform {get{return platform.Platform;}}
        internal NetworkProviderFactory(IPlatformSdkProviderFactory platform,Func<INetwork> createNetwork)
        {this.platform=platform;this.createNetwork=createNetwork;}
        public IAuth CreateAuth(Action<Action> dispatch){return platform.CreateAuth(dispatch);}
        public IStorage CreateStorage(){return platform.CreateStorage();}
        public IShare CreateShare(){var factory=platform as IShareProviderFactory;return factory==null?null:factory.CreateShare();}
        public IRewardedAds CreateRewardedAds(){var factory=platform as IRewardedAdsProviderFactory;return factory==null?null:factory.CreateRewardedAds();}
        public IPay CreatePay(Action<Action> dispatch){var factory=platform as IPayProviderFactory;return factory==null?null:factory.CreatePay(dispatch);}
        public INetwork CreateNetwork(){return createNetwork();}
    }
}
