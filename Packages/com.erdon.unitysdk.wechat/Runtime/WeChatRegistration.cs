#if UNITY_5_3_OR_NEWER
using UnityEngine;
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
