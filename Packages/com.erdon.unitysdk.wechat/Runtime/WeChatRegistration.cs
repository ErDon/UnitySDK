#if UNITY_5_3_OR_NEWER
using UnityEngine;
// Run the linker over this package even when game code only references the core registry.
[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]
namespace ErDon.UnitySDK.WeChat
{
    static class WeChatRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            SdkPlatformRegistry.Register("WeChat", () => new WeChatRuntime(),
                (game, environment, exchange) => new WeChatProviderFactory(game, environment, exchange));
        }
    }
}
#endif
