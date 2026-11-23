using System;

namespace ErDon.UnitySDK.WeChat
{
    /// <summary>Use only after official WeChat SDK initialization. Creates fresh, SDK-owned capabilities.</summary>
    public sealed class WeChatProviderFactory : ISdkProviderFactory, IShareProviderFactory
    {
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
            if (mainThreadDispatch == null) throw new ArgumentNullException("mainThreadDispatch");
            if (createSessionExchange == null) throw new InvalidOperationException("WeChat login requires a backend session exchange factory");
            var exchange = createSessionExchange();
            if (exchange == null) throw new InvalidOperationException("Session exchange factory returned null");
            try { return new VerifiedAuth(new WeChatCredentialProvider(), exchange, mainThreadDispatch); }
            catch { try { exchange.Dispose(); } catch { } throw; }
        }
        public IStorage CreateStorage() { return new WeChatStorage(gameId, environment); }
        public IShare CreateShare() { return new WeChatShare(); }
    }
}

