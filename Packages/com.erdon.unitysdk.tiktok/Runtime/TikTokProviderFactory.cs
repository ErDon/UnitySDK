using System;

namespace ErDon.UnitySDK.TikTok
{
    /// <summary>Use only after official TTSDK initialization. Creates fresh, SDK-owned capabilities.</summary>
    public sealed class TikTokProviderFactory : IPlatformSdkProviderFactory, IShareProviderFactory, IRewardedAdsProviderFactory, IAnalyticsProviderFactory, IPayProviderFactory
    {
        public string Platform { get { return "TikTok"; } }
        readonly string gameId;
        readonly string environment;
        readonly Func<ISessionExchange> createSessionExchange;
        readonly Func<Action<Action>, IPay> createPayment;

        // The exchange factory is optional when login is disabled. Never capture a shared exchange instance.
        public TikTokProviderFactory(string gameId, string environment, Func<ISessionExchange> createSessionExchange = null,
            Func<Action<Action>, IPay> createPayment = null)
        {
            if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(environment))
                throw new ArgumentException("Game and environment are required");
            this.gameId = gameId;
            this.environment = environment;
            this.createSessionExchange = createSessionExchange;
            this.createPayment = createPayment;
        }
        // 必须显式注入服务端支付组合；勾选支付不等于拥有平台签名和发货能力。
        // 返回全新实例交 SDK 释放，避免跨启动共享已释放的支付对象。
        public IPay CreatePay(Action<Action> mainThreadDispatch) {
#if ERDON_EXCLUDE_PAYMENTS && !UNITY_EDITOR
            throw new InvalidOperationException("CONFIG.MODULE_NOT_COMPILED: PAYMENTS");
#else
            if(mainThreadDispatch==null)throw new ArgumentNullException("mainThreadDispatch");
            if(createPayment==null)throw new InvalidOperationException("TikTok payment requires an explicit backend payment factory");
            var payment=createPayment(mainThreadDispatch);
            if(payment==null)throw new InvalidOperationException("Payment factory returned null");
            return payment;
#endif
        }
        public IAuth CreateAuth(Action<Action> mainThreadDispatch)
        {
#if ERDON_EXCLUDE_LOGIN && !UNITY_EDITOR
            throw new InvalidOperationException("CONFIG.MODULE_NOT_COMPILED: LOGIN");
#else
            if (mainThreadDispatch == null) throw new ArgumentNullException("mainThreadDispatch");
            if (createSessionExchange == null) throw new InvalidOperationException("TikTok login requires a backend session exchange factory");
            var exchange = createSessionExchange();
            if (exchange == null) throw new InvalidOperationException("Session exchange factory returned null");
            try { return new VerifiedAuth(new TikTokCredentialProvider(), exchange, mainThreadDispatch); }
            catch { try { exchange.Dispose(); } catch { } throw; }
#endif
        }
        public IAnalytics CreateAnalytics() {
#if ERDON_EXCLUDE_ANALYTICS && !UNITY_EDITOR
            throw new InvalidOperationException("CONFIG.MODULE_NOT_COMPILED: ANALYTICS");
#else
            return new TikTokAnalytics();
#endif
        }
        public IStorage CreateStorage() {
#if ERDON_EXCLUDE_STORAGE && !UNITY_EDITOR
            throw new InvalidOperationException("CONFIG.MODULE_NOT_COMPILED: STORAGE");
#else
            return new TikTokStorage(gameId, environment);
#endif
        }
        public IRewardedAds CreateRewardedAds() {
#if ERDON_EXCLUDE_ADS && !UNITY_EDITOR
            throw new InvalidOperationException("CONFIG.MODULE_NOT_COMPILED: ADS");
#else
            return new TikTokRewardedAds();
#endif
        }
        public IShare CreateShare() {
#if ERDON_EXCLUDE_SHARE && !UNITY_EDITOR
            throw new InvalidOperationException("CONFIG.MODULE_NOT_COMPILED: SHARE");
#else
            return new TikTokShare();
#endif
        }
    }
}
