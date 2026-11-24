using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Scripting;

namespace ErDon.UnitySDK.Android
{
    [Serializable]
    public sealed class AndroidReply
    {
        public int schemaVersion;
        public bool success, completed;
        public string value, code, reason, platformCode;
        public SdkError Error { get { return success ? null : new SdkError(code ?? "BRIDGE.INVALID_RESPONSE", reason ?? "Invalid native result", platformCode, null); } }
        public static AndroidReply Fail(string code, string reason) { return new AndroidReply { schemaVersion=1, code=code, reason=reason }; }
        public static AndroidReply Parse(string json)
        {
            try {
                if(json==null || json.Length>2097152) return Fail("BRIDGE.INVALID_RESPONSE","Native response exceeds limit");
                var result=JsonUtility.FromJson<AndroidReply>(json);
                if(result!=null && result.schemaVersion==1 && (result.success ? string.IsNullOrEmpty(result.code) : !string.IsNullOrEmpty(result.code))) return result;
            } catch { }
            return Fail("BRIDGE.INVALID_RESPONSE","Malformed native response");
        }
    }
    public interface IAndroidBridge : IDisposable
    {
        AndroidReply Storage(string operation, string key, string value);
        Action Call(string method, string payload, Action<AndroidReply> callback);
    }
    // One bridge belongs to one capability. Java close releases only that capability's providers.
    public sealed class AndroidBridge : IAndroidBridge
    {
        readonly Action<Action> post;
        readonly Dictionary<string,Completion> pending=new Dictionary<string,Completion>();
        readonly object gate=new object();
        bool disposed;
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaObject native;
        NativeLogger nativeLogger;
#endif
        public AndroidBridge(string gameId,string environment,string module,Action<Action> mainThreadPost)
        {
            if(mainThreadPost==null) throw new ArgumentNullException("mainThreadPost");
            post=mainThreadPost;
#if UNITY_ANDROID && !UNITY_EDITOR
            using(var player=new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using(var activity=player.GetStatic<AndroidJavaObject>("currentActivity")) {
                nativeLogger=new NativeLogger();
                native=new AndroidJavaObject("com.erdon.sdk.android.UnityBridge",activity,gameId,environment,module,nativeLogger);
            }
#endif
        }
        public AndroidReply Storage(string operation,string key,string value)
        {
            if(disposed) return AndroidReply.Fail("SDK.DISPOSED","Android bridge disposed");
#if UNITY_ANDROID && !UNITY_EDITOR
            try { return AndroidReply.Parse(native.Call<string>("storage",operation,key,value)); }
            catch { return AndroidReply.Fail("BRIDGE.JNI_FAILED","Android storage invocation failed"); }
#else
            return AndroidReply.Fail("PLATFORM.UNAVAILABLE","Android device required");
#endif
        }
        public Action Call(string method,string payload,Action<AndroidReply> callback)
        {
            if(callback==null) throw new ArgumentNullException("callback");
            if(disposed) { callback(AndroidReply.Fail("SDK.DISPOSED","Android bridge disposed"));return ()=>{}; }
            var id=Guid.NewGuid().ToString("N");
            var completion=new Completion(result=> {
                // JNI callbacks run on Android UI threads, never invoke game code there.
                post(()=> {
                    lock(gate) { if(disposed || !pending.Remove(id)) return; }
                    callback(result);
                });
            });
            lock(gate) pending.Add(id,completion);
#if UNITY_ANDROID && !UNITY_EDITOR
            try { native.Call("call",id,method,payload,completion); }
            catch { completion.Finish(AndroidReply.Fail("BRIDGE.JNI_FAILED","Android invocation failed")); }
#else
            completion.Finish(AndroidReply.Fail("PLATFORM.UNAVAILABLE","Android device required"));
#endif
            return ()=> {
#if UNITY_ANDROID && !UNITY_EDITOR
                if(!disposed) try { native.Call("cancel",id); } catch { }
#endif
                completion.Finish(AndroidReply.Fail("OPERATION.CANCELLED","Local wait cancelled"));
            };
        }
        public void Dispose()
        {
            lock(gate) { if(disposed)return;disposed=true;pending.Clear(); }
#if UNITY_ANDROID && !UNITY_EDITOR
            try { native.Call("close"); } finally { native.Dispose();native=null;nativeLogger=null; }
#endif
        }
        [Preserve]
        sealed class Completion
#if UNITY_ANDROID && !UNITY_EDITOR
            : AndroidJavaProxy
#endif
        {
            Action<AndroidReply> callback;int finished;
            public Completion(Action<AndroidReply> callback)
#if UNITY_ANDROID && !UNITY_EDITOR
                :base("com.erdon.sdk.android.UnityBridge$Callback")
#endif
            { this.callback=callback; }
            [Preserve] public void complete(string json) { Finish(AndroidReply.Parse(json)); }
            public void Finish(AndroidReply result) {
                if(Interlocked.Exchange(ref finished,1)!=0)return;
                var action=callback;callback=null;action(result);
            }
        }
#if UNITY_ANDROID && !UNITY_EDITOR
        [Preserve]
        sealed class NativeLogger : AndroidJavaProxy
        {
            public NativeLogger():base("com.erdon.sdk.android.UnityBridge$Logger") { }
            // Unity aggregate already formats/routes the operation log; suppress duplicate native lines.
            [Preserve] public void write(string line) { }
        }
#endif
    }
}
