using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
namespace ErDon.UnitySDK
{
    public sealed class StartupResult
    {
        public string CallId {get;internal set;}
        public string TraceId {get{return CallId;}}
        public SdkError Error {get;internal set;}
        public bool Success {get{return Error==null;}}
    }
    /// <summary>Owns one startup attempt and runtime bridge. Start/Tick/Cancel/Dispose on its creating thread.</summary>
    public sealed class SdkStartup : IDisposable
    {
        readonly SdkConfig config;
        readonly IPlatformRuntime runtime;
        readonly IPlatformSdkProviderFactory factory;
        readonly Action<Action> dispatch;
        readonly Action<SdkLogLevel,string> engineLog;
        readonly Action<StartupResult> callback;
        readonly int thread, timeout;
        readonly string call=Guid.NewGuid().ToString("N");
        readonly ConcurrentQueue<Action> completions=new ConcurrentQueue<Action>();
        readonly Stopwatch timer=new Stopwatch();
        int received;
        bool started, finished, released, logging;
        public bool IsFinished {get{return finished;}}
        public SdkStartup(SdkConfig config,IPlatformRuntime runtime,IPlatformSdkProviderFactory factory,
            Action<Action> dispatch,Action<SdkLogLevel,string> engineLog,Action<StartupResult> callback,int timeoutMilliseconds=15000)
        {
            if(config==null||runtime==null||factory==null||dispatch==null||engineLog==null||callback==null)throw new ArgumentNullException();
            if(timeoutMilliseconds<=0)throw new ArgumentOutOfRangeException("timeoutMilliseconds");
            if(config.Platform!=runtime.Platform||config.Platform!=factory.Platform)throw new InvalidOperationException("CONFIG.PLATFORM_MISMATCH");
            this.config=new SdkConfig{Platform=config.Platform,LoginEnabled=config.LoginEnabled,StorageEnabled=config.StorageEnabled,
                ShareEnabled=config.ShareEnabled,PayEnabled=config.PayEnabled,RewardedAdsEnabled=config.RewardedAdsEnabled,NetworkEnabled=config.NetworkEnabled,AnalyticsEnabled=config.AnalyticsEnabled,LogCallback=config.LogCallback};
            this.runtime=runtime;this.factory=factory;this.dispatch=dispatch;this.engineLog=engineLog;this.callback=callback;
            timeout=timeoutMilliseconds;thread=Thread.CurrentThread.ManagedThreadId;
        }
        void CheckThread(){if(Thread.CurrentThread.ManagedThreadId!=thread||logging)throw new InvalidOperationException("Startup requires its owner thread and cannot be reentered from logs");}
        void Log(string stage,string value){logging=true;try{SDK.WriteStartupLog(config,engineLog,call,stage,value,timeout);}finally{logging=false;}}
        public void Start()
        {
            CheckThread();if(started||finished)throw new InvalidOperationException("Startup attempt already used");
            started=true;timer.Start();Log("Start","Pending");
            Action<SdkError> done=error=>{
                if(Interlocked.Exchange(ref received,1)!=0)return;
                var snapshot=error==null?null:error.Snapshot();
                if(timer.ElapsedMilliseconds>=timeout)snapshot=new SdkError("INIT.TIMEOUT","Platform initialization timed out");
                completions.Enqueue(()=>CompletePlatform(snapshot));
            };
            try{runtime.Initialize(done);}catch{done(new SdkError("INIT.PROVIDER_EXCEPTION","Platform initialization invocation failed"));}
        }
        public void Tick()
        {
            CheckThread();if(!started||finished)return;
            Action next;if(completions.TryDequeue(out next)){next();return;}
            if(timer.ElapsedMilliseconds>=timeout)Finish(new SdkError("INIT.TIMEOUT","Platform initialization timed out"));
        }
        void CompletePlatform(SdkError error)
        {
            if(finished)return;
            if(error!=null){Finish(error);return;}
            try{
                if(!runtime.IsReady){Finish(new SdkError("INIT.NOT_READY","Platform callback did not establish readiness"));return;}
                SDK.InitializeWithFactory(config,factory,dispatch,engineLog);
            }catch{Finish(new SdkError("INIT.COMPOSITION_FAILED","SDK capability construction failed"));return;}
            Finish(null);
        }
        void Release(){if(released)return;released=true;try{runtime.Dispose();}catch{Log("Dispose","SDK.DISPOSE_FAILED");}}
        void Finish(SdkError error)
        {
            if(finished)return;finished=true;
            if(error!=null){error=error.Snapshot();error.CallId=call;error.Operation="SDK.Startup";error.Stage="Initialization";}
            Release();Log("Complete",error==null?"Success":error.Code);
            try{callback(new StartupResult{CallId=call,Error=error});}catch{Log("Callback","CALLBACK.EXCEPTION");}
        }
        public void Cancel(){CheckThread();if(!finished)Finish(new SdkError("INIT.CANCELLED","Caller cancelled local startup wait"));}
        public void Dispose(){CheckThread();finished=true;Release();}
    }
}
