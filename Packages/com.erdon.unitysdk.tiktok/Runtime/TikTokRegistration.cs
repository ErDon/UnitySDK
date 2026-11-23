#if UNITY_5_3_OR_NEWER
using UnityEngine;
// Run the linker over this package even when game code only references the core registry.
[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]
namespace ErDon.UnitySDK.TikTok
{
    static class TikTokRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            SdkPlatformRegistry.Register("TikTok", () => new TikTokRuntime(),
                (game, environment, exchange) => new TikTokProviderFactory(game, environment, exchange), supportsPayments:true, requiresPaymentFactory:true);
        }
    }
}
#endif
