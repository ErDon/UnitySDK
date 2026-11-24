using ErDon.UnitySDK;
using UnityEngine;
public sealed class AndroidStartupExample : MonoBehaviour
{
    void Start() {
        var config=new SdkRuntimeConfiguration {
            platform="Android",gameId="replace-game-id",appId=Application.identifier,
            production=false,login=false,storage=true,share=true
        };
        UnitySdkHost.BeginConfiguredStartup(config,null,result=> {
            if(!result.Success){Debug.LogError(result.Error.Code+": "+result.Error.Reason);return;}
            SDK.SetStorageSync("welcome","hello");
            Debug.Log(SDK.GetStorageSync("welcome").Value);
        });
    }
    // Bind to a UI button; never show the share sheet automatically on startup.
    public void Share(){SDK.Share(new ShareRequest("Hello from my game"),result=>Debug.Log(result.Success?"Share launched":result.Error.Code));}
    void OnDestroy(){UnitySdkHost.Shutdown();}
}
