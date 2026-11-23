using System;
using System.Collections.Generic;
using ErDon.UnitySDK;
class LifecycleTests
{
    sealed class Provider : IAuth {
        public Action<LoginResult> Pending;
        public Action Disposing;
        public int Disposes;
        public void Login(Action<LoginResult> callback) { Pending=callback; }
        public void Dispose() { Disposes++; if(Disposing!=null)Disposing(); }
    }
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static void Main()
    {
        var logs=new List<string>(); var queue=new Queue<Action>();
        Action<Action> post=a=>queue.Enqueue(a);
        Action<SdkLogLevel,string> logger=(level,line)=>logs.Add(line);
        var old=new Provider(); SDK.Initialize(new SdkConfig(),old,post,logger);
        int delivered=0; SDK.Login(r=>delivered++);
        old.Pending(LoginResult.Ok("session")); // Completion is queued, not delivered.
        bool rejected=false;
        try{SDK.Initialize(new SdkConfig(),old,post,logger);}catch(ArgumentException){rejected=true;}
        Check(rejected && old.Disposes==0,"Reuse rejected before altering active instance");
        var next=new Provider();
        bool reentryRejected=false;
        old.Disposing=()=>{
            try{SDK.Initialize(new SdkConfig(),new Provider(),post,logger);}
            catch(InvalidOperationException){reentryRejected=true;}
            throw new Exception("PRIVATE_DISPOSE_DETAILS");
        };
        SDK.Initialize(new SdkConfig(),next,post,logger);
        while(queue.Count!=0)queue.Dequeue()();
        Check(delivered==0 && old.Disposes==1 && reentryRejected,"Reinit invalidates queued result and rejects disposal reentry");
        Check(logs.Exists(s=>s.Contains("SDK.DISPOSE_FAILED"))&&!logs.Exists(s=>s.Contains("PRIVATE_DISPOSE_DETAILS")),"Dispose failure formatted without details");
        int queued=queue.Count; old.Pending(LoginResult.Ok("late"));
        Check(queue.Count==queued,"Old provider cannot enqueue new work after shutdown");
        LoginResult result=null; SDK.Login(r=>result=r); next.Pending(LoginResult.Ok(" "));
        while(queue.Count!=0)queue.Dequeue()();
        Check(result.Error.Code=="AUTH.INVALID_SESSION","Empty provider session rejected");
        // User callback is allowed to initialize a new instance and begin a new request.
        var third=new Provider(); LoginResult final=null;
        SDK.Login(r=>{SDK.Initialize(new SdkConfig(),third,post,logger);SDK.Login(x=>final=x);});
        next.Pending(LoginResult.Ok("valid")); while(queue.Count!=0)queue.Dequeue()();
        third.Pending(LoginResult.Ok("third")); while(queue.Count!=0)queue.Dequeue()();
        Check(final!=null && final.Session=="third" && next.Disposes==1,"User callback reinitialization remains supported");
        SDK.Shutdown(); SDK.Shutdown(); Check(third.Disposes==1,"Idempotent shutdown");
        Console.WriteLine("PASS: provider ownership, lifecycle reentry, stale queued completion, empty session and disposal diagnostics");
    }
}
