#if UNITY_5_3_OR_NEWER
using UnityEngine;
namespace ErDon.UnitySDK.TikTok
{
    static class TikTokRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            SdkPlatformRegistry.Register("TikTok", () => new TikTokRuntime(),
                (game, environment, exchange) => new TikTokProviderFactory(game, environment, exchange));
        }
    }
}
#endif
