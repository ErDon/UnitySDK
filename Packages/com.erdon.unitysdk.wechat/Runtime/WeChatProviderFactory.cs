using System;

namespace ErDon.UnitySDK.WeChat
{
    /// <summary>Use only after official WeChat SDK initialization. Creates fresh, SDK-owned capabilities.</summary>
    public sealed class WeChatProviderFactory : IPlatformSdkProviderFactory, IShareProviderFactory, IRewardedAdsProviderFactory, IAnalyticsProviderFactory
    {
        public string Platform { get { return "WeChat"; } }
        readonly string gameId;
        readonly string environment;
        readonly Func<ISessionExchange> createSessionExchange;

        // The exchange factory is optional when login is disabled. Never capture a shared exchange instance.
        public WeChatProviderFactory(string gameId, string environment, Func<ISessionExchange> createSessionExchange = null)
        {
            if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(environment))
                throw new ArgumentException("Game and environment are required");
            this.gameId = gameId;
            this.environment = environment;
            this.createSessionExchange = createSessionExchange;
        }
        public IAuth CreateAuth(Action<Action> mainThreadDispatch)
        {
#if ERDON_EXCLUDE_LOGIN && !UNITY_EDITOR
            throw new InvalidOperationException("CONFIG.MODULE_NOT_COMPILED: LOGIN");
#else
            if (mainThreadDispatch == null) throw new ArgumentNullException("mainThreadDispatch");
            if (createSessionExchange == null) throw new InvalidOperationException("WeChat login requires a backend session exchange factory");
            var exchange = createSessionExchange();
            if (exchange == null) throw new InvalidOperationException("Session exchange factory returned null");
            try { return new VerifiedAuth(new WeChatCredentialProvider(), exchange, mainThreadDispatch); }
            catch { try { exchange.Dispose(); } catch { } throw; }
#endif
        }
        public IStorage CreateStorage() {
#if ERDON_EXCLUDE_STORAGE && !UNITY_EDITOR
            throw new InvalidOperationException("CONFIG.MODULE_NOT_COMPILED: STORAGE");
#else
            return new WeChatStorage(gameId, environment);
#endif
        }
        public IAnalytics CreateAnalytics() {
#if ERDON_EXCLUDE_ANALYTICS && !UNITY_EDITOR
            throw new InvalidOperationException("CONFIG.MODULE_NOT_COMPILED: ANALYTICS");
#else
            return new WeChatAnalytics();
#endif
        }
        public IRewardedAds CreateRewardedAds() {
#if ERDON_EXCLUDE_ADS && !UNITY_EDITOR
            throw new InvalidOperationException("CONFIG.MODULE_NOT_COMPILED: ADS");
#else
            return new WeChatRewardedAds();
#endif
        }
        public IShare CreateShare() {
#if ERDON_EXCLUDE_SHARE && !UNITY_EDITOR
            throw new InvalidOperationException("CONFIG.MODULE_NOT_COMPILED: SHARE");
#else
            return new WeChatShare();
#endif
        }
    }
}
