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
        Check(logs[1].Contains("callId=") && logs[1].Contains("params={}"),"log fields");
        SDK.Login(r=>calls++); SDK.Shutdown(); p.Pending(LoginResult.Ok("SECRET"));
        Check(calls==1,"stale completion");
        int fallback=0;
        SDK.Initialize(new SdkConfig { LoginEnabled=false, LogCallback=(l,s)=>{throw new Exception();} },p,a=>a(),(l,s)=>fallback++);
        SDK.Login(r=>Check(r.Error.Code=="MODULE.DISABLED","disabled"));
        Check(fallback>=3,"log fallback");
        SDK.Shutdown(); SDK.Login(r=>Check(r.Error.Code=="SDK.NOT_INITIALIZED","uninitialized"));
        Console.WriteLine("PASS: duplicate/stale callback, credential redaction, log fields, fallback, disabled, uninitialized");
    }
}
