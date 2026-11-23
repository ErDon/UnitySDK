using System;
using System.Collections.Concurrent;
using UnityEngine;
namespace ErDon.UnitySDK
{
    public sealed class UnitySdkHost : MonoBehaviour
    {
        readonly ConcurrentQueue<Action> queue = new ConcurrentQueue<Action>();
        static UnitySdkHost instance;
        public static void Initialize(SdkConfig config, IAuth provider, IStorage storageProvider = null)
        {
            if (instance == null) {
                var go = new GameObject("ErDon SDK");
                instance = go.AddComponent<UnitySdkHost>();
                DontDestroyOnLoad(go);
            }
            var host = instance;
            SDK.Initialize(config, provider, a => host.queue.Enqueue(a), WriteLog, storageProvider);
        }
        static void WriteLog(SdkLogLevel level, string message)
        {
            if (level == SdkLogLevel.Error) Debug.LogError(message);
            else if (level == SdkLogLevel.Warning) Debug.LogWarning(message);
            else Debug.Log(message);
        }
        void Update()
        {
            SDK.Tick();
            Action action;
            while (queue.TryDequeue(out action)) {
                try { action(); } catch (Exception) { Debug.LogError("SDK callback threw an exception; details omitted to protect credentials."); }
            }
        }
        void OnDestroy() { if (instance == this) { SDK.Shutdown(); instance = null; } }
    }
}
