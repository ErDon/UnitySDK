using System;

namespace ErDon.UnitySDK.TikTok
{
    /// <summary>Use only after official TTSDK initialization. Creates fresh, SDK-owned capabilities.</summary>
    public sealed class TikTokProviderFactory : ISdkProviderFactory, IShareProviderFactory
    {
        readonly string gameId;
        readonly string environment;
        readonly Func<ISessionExchange> createSessionExchange;

        // The exchange factory is optional when login is disabled. Never capture a shared exchange instance.
        public TikTokProviderFactory(string gameId, string environment, Func<ISessionExchange> createSessionExchange = null)
        {
            if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(environment))
                throw new ArgumentException("Game and environment are required");
            this.gameId = gameId;
            this.environment = environment;
            this.createSessionExchange = createSessionExchange;
        }
        public IAuth CreateAuth(Action<Action> mainThreadDispatch)
        {
            if (mainThreadDispatch == null) throw new ArgumentNullException("mainThreadDispatch");
            if (createSessionExchange == null) throw new InvalidOperationException("TikTok login requires a backend session exchange factory");
            var exchange = createSessionExchange();
            if (exchange == null) throw new InvalidOperationException("Session exchange factory returned null");
            try { return new VerifiedAuth(new TikTokCredentialProvider(), exchange, mainThreadDispatch); }
            catch { try { exchange.Dispose(); } catch { } throw; }
        }
        public IStorage CreateStorage() { return new TikTokStorage(gameId, environment); }
        public IShare CreateShare() { return new TikTokShare(); }
    }
}

