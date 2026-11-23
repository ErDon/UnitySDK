using System;
using System.Collections.Generic;
using ErDon.UnitySDK;
class Tests
{
    class Provider : IAuth {
        public Action<LoginResult> Pending;
        public void Login(Action<LoginResult> callback) { Pending = callback; }
        public void Dispose() { }
    }
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Main() {
        var logs = new List<string>(); var p = new Provider(); int calls = 0;
        SDK.Initialize(new SdkConfig { Platform="Test", LogCallback=(l,s)=>logs.Add(s) }, p, a=>a(), (l,s)=>{});
        SDK.Login(r=>calls++);
        p.Pending(LoginResult.Ok("SECRET")); p.Pending(LoginResult.Ok("SECRET"));
        Check(calls==1,"duplicate completion");
        Check(!string.Join("",logs).Contains("SECRET"),"credential leak");
        Check(logs[1].Contains("callId=") && logs[1].Contains("timeoutMilliseconds"),"log fields");
        SDK.Login(r=>calls++); SDK.Shutdown(); p.Pending(LoginResult.Ok("SECRET"));
        Check(calls==1,"stale completion");
        int fallback=0;
        SDK.Initialize(new SdkConfig { LoginEnabled=false, LogCallback=(l,s)=>{throw new Exception();} },p,a=>a(),(l,s)=>fallback++);
        string disabledCode=null; SDK.Login(r=>disabledCode=r.Error.Code);
        Check(disabledCode=="MODULE.DISABLED","disabled");
        Check(fallback>=3,"log fallback");
        SDK.Shutdown(); string uninitializedCode=null; SDK.Login(r=>uninitializedCode=r.Error.Code);
        Check(uninitializedCode=="SDK.NOT_INITIALIZED","uninitialized");
        var pending = new Provider(); var queue = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        SDK.Initialize(new SdkConfig(), pending, a=>queue.Enqueue(a), (l,s)=>{});
        string code = null; int finishes = 0;
        SDK.Login(r=>{code=r.Error.Code; finishes++;}, 1000);
        SDK.CancelLogin(); pending.Pending(LoginResult.Ok("late"));
        Action job; while(queue.TryDequeue(out job)) job();
        Check(code=="AUTH.CANCELLED" && finishes==1,"cancel/late callback");
        code=null;
        SDK.Login(r=>code=r.Error.Code, 20);
        var deadline=System.Diagnostics.Stopwatch.StartNew();
        while(queue.IsEmpty && deadline.ElapsedMilliseconds<2000) { System.Threading.Thread.Sleep(5); SDK.Tick(); }
        while(queue.TryDequeue(out job)) job();
        Check(code=="AUTH.TIMEOUT","timeout");
        SDK.Login(r=>{throw new Exception("SECRET");},1000);
        pending.Pending(LoginResult.Ok("SECRET"));
        while(queue.TryDequeue(out job)) job();
        SDK.Shutdown();
        var original = new SdkError("AUTH.PERMISSION_DENIED", "Denied", "42", "Sanitized denial");
        SDK.Initialize(new SdkConfig(), pending, a=>queue.Enqueue(a), (l,s)=>logs.Add(s));
        LoginResult failure=null;
        SDK.Login(r=>failure=r);
        pending.Pending(LoginResult.Fail(original));
        while(queue.TryDequeue(out job)) job();
        Check(failure.CallId==original.CallId && original.Operation=="SDK.Login" && original.PlatformCode=="42", "error correlation");
        Check(logs[logs.Count-1].Contains(failure.CallId),"log correlation");
        SDK.Shutdown();
        Console.WriteLine("PASS: cancellation, timeout, user callback exception; duplicate/stale callback, credential redaction, log fields, fallback, disabled, uninitialized");
    }
}
