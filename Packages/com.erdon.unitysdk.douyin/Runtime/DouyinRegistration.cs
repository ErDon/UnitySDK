#if UNITY_5_3_OR_NEWER
using UnityEngine;
namespace ErDon.UnitySDK.Douyin
{
    static class DouyinRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            SdkPlatformRegistry.Register("Douyin", () => new DouyinRuntime(),
                (game, environment, exchange) => new DouyinProviderFactory(game, environment, exchange));
        }
    }
}
#endif
