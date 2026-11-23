using System;
using ErDon.UnitySDK;
using UnityEngine;

// One persistent owner per game. Import the selected platform adapter separately.
public sealed class ConfiguredSdkExample : MonoBehaviour
{
    public TextAsset configurationJson;
    [Tooltip("Your game server HTTPS session endpoint; never a platform app secret.")]
    public string sessionEndpoint;
    static ConfiguredSdkExample owner;
    bool ownsSdk, ready;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOwner() { owner = null; }

    void Start()
    {
        if (owner != null && owner != this) {
            Debug.LogWarning("SDK example already has an owner; duplicate component ignored.");
            enabled = false;
            return;
        }
        if (transform.parent != null) {
            Debug.LogError("Place ConfiguredSdkExample on a root GameObject.");
            return;
        }
        SdkRuntimeConfiguration config;
        try {
            if (configurationJson == null) throw new InvalidOperationException("CONFIG.JSON_REQUIRED");
            config = JsonUtility.FromJson<SdkRuntimeConfiguration>(configurationJson.text);
            if (config == null) throw new InvalidOperationException("CONFIG.JSON_REQUIRED");
            var issues = config.Validate();
            if (issues.Count != 0) { Debug.LogError(string.Join(",", issues.ToArray())); return; }
            if (config.platform == "Mock") throw new InvalidOperationException("Use the core Mock Login sample for Editor simulation.");
            if (config.login) {
                Uri endpoint;
                if (!Uri.TryCreate(sessionEndpoint, UriKind.Absolute, out endpoint) || endpoint.Scheme != "https"
                    || !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Fragment))
                    throw new InvalidOperationException("CONFIG.HTTPS_SESSION_ENDPOINT_REQUIRED");
            }
        } catch {
            Debug.LogError("SDK example configuration rejected. Check JSON, platform, module flags and HTTPS session endpoint.");
            return;
        }
        owner = this;
        ownsSdk = true;
        DontDestroyOnLoad(gameObject);
        Func<ISessionExchange> exchange = null;
        Func<INetwork> network = null;
#if !ERDON_EXCLUDE_LOGIN || UNITY_EDITOR
        var endpointSnapshot = sessionEndpoint;
        if (config.login) exchange = () => new UnitySessionExchange(this, endpointSnapshot,
            config.gameId, config.appId, config.Environment);
#endif
#if !ERDON_EXCLUDE_NETWORK || UNITY_EDITOR
        if (config.network) network = () => new UnityNetwork(this);
#endif
        try {
            UnitySdkHost.BeginConfiguredStartup(config, exchange, result => {
                ready = result.Success;
                Debug.Log(result.Success ? "SDK ready; call LoginFromGame when the game requests login."
                    : result.Error.Code + " / " + result.Error.Reason + " callId=" + result.CallId);
            }, createNetwork: network);
        } catch {
            Debug.LogError("SDK startup rejected. Check the platform package, build defines and backend factory configuration.");
        }
    }

    // Connect this to the game's login button. Startup itself sends no login request.
    public void LoginFromGame()
    {
        if (!ownsSdk || !ready) { Debug.LogWarning("SDK is not ready."); return; }
        SDK.Login(result => {
            if (!result.Success) {
                Debug.LogWarning(result.Error.Code + " / " + result.Error.Reason + " callId=" + result.CallId);
                return;
            }
            // Pass result.Session to your game session layer; never log it.
            Debug.Log("Game session received; callId=" + result.CallId);
        });
    }

    void OnDestroy()
    {
        if (!ownsSdk || owner != this) return;
        ready = false;
        UnitySdkHost.Shutdown();
        owner = null;
    }
}
