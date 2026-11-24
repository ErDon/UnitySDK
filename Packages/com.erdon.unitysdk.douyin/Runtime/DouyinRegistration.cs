#if UNITY_5_3_OR_NEWER
using UnityEngine;
// Run the linker over this package even when game code only references the core registry.
[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]
namespace ErDon.UnitySDK.Douyin
{
    static class DouyinRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            SdkPlatformRegistry.Register("Douyin", () => new DouyinRuntime(),
                (game, environment, exchange) => new DouyinProviderFactory(game, environment, exchange),
                supportsPayments: true, requiresPaymentFactory: true);
        }
    }
}
#endif
