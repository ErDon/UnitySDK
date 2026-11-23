using System;
using System.Collections.Concurrent;
using UnityEngine;
namespace ErDon.UnitySDK
{
    public sealed class UnitySdkHost : MonoBehaviour
    {
        readonly ConcurrentQueue<Action> queue = new ConcurrentQueue<Action>();
        static UnitySdkHost instance;
        SdkStartup startup;
        bool replacingStartup;
        public static void BeginStartup(SdkConfig config, IPlatformRuntime runtime, IPlatformSdkProviderFactory factory,
            Action<StartupResult> callback, int timeoutMilliseconds = 15000)
        {
            SDK.CheckHostLifecycleEntry();
            var host = GetOrCreate();
            if(host.replacingStartup)throw new InvalidOperationException("Startup replacement cannot be reentered");
            var next = new SdkStartup(config, runtime, factory, a => host.queue.Enqueue(a), WriteLog, callback, timeoutMilliseconds);
            host.ReplaceStartup(next);
            next.Start();
        }
        public static void CancelStartup()
        {
            SDK.CheckHostLifecycleEntry();
            if(instance != null && instance.startup != null) instance.startup.Cancel();
        }
        public static void Shutdown()
        {
            SDK.CheckHostLifecycleEntry();
            if(instance != null) instance.ReplaceStartup(null);
            SDK.Shutdown();
        }
        void ReplaceStartup(SdkStartup next)
        {
            if(replacingStartup)throw new InvalidOperationException("Startup replacement cannot be reentered");
            replacingStartup=true;
            try { var old=startup; startup=next; if(old!=null)old.Dispose(); }
            finally { replacingStartup=false; }
        }
        public static void InitializeWithFactory(SdkConfig config, ISdkProviderFactory factory)
        {
            SDK.CheckHostLifecycleEntry();
            var host = GetOrCreate();
            host.ReplaceStartup(null);
            SDK.InitializeWithFactory(config, factory, a => host.queue.Enqueue(a), WriteLog);
        }
        public static void Initialize(SdkConfig config, IAuth provider, IStorage storageProvider = null, IShare shareProvider = null, IPay payProvider = null, IRewardedAds adsProvider = null)
        {
            SDK.CheckHostLifecycleEntry();
            var host = GetOrCreate();
            host.ReplaceStartup(null);
            SDK.Initialize(config, provider, a => host.queue.Enqueue(a), WriteLog, storageProvider, shareProvider, payProvider, adsProvider);
        }
        static UnitySdkHost GetOrCreate()
        {
            if (instance == null) {
                var go = new GameObject("ErDon SDK");
                instance = go.AddComponent<UnitySdkHost>();
                DontDestroyOnLoad(go);
            }
            return instance;
        }
        static void WriteLog(SdkLogLevel level, string message)
        {
            if (level == SdkLogLevel.Error) Debug.LogError(message);
            else if (level == SdkLogLevel.Warning) Debug.LogWarning(message);
            else Debug.Log(message);
        }
        void Update()
        {
            var current=startup;
            if(current!=null)current.Tick();
            SDK.Tick();
            Action action;
            while (queue.TryDequeue(out action)) {
                try { action(); } catch (Exception) { Debug.LogError("SDK callback threw an exception; details omitted to protect credentials."); }
            }
        }
        void OnDestroy() { if (instance == this) { ReplaceStartup(null); SDK.Shutdown(); instance = null; } }
    }
}
