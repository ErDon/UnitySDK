using System;
using System.Collections.Generic;
using System.Threading;
using ErDon.UnitySDK;
class ShareTests
{
    sealed class Provider : IShare {
        public Action<ShareResult> Callback;
        public int Calls, Disposals;
        public void Share(ShareRequest request, Action<ShareResult> callback) { Calls++; Callback=callback; }
        public void Dispose() { Disposals++; }
    }
    sealed class Factory : ISdkProviderFactory, IShareProviderFactory {
        public Provider Provider;
        public int Calls;
        public IAuth CreateAuth(Action<Action> post) { throw new Exception("disabled auth"); }
        public IStorage CreateStorage() { throw new Exception("disabled storage"); }
        public IShare CreateShare() { Calls++; return Provider; }
    }
    static void Check(bool ok,string reason) { if(!ok)throw new Exception(reason); }
    static void Main() {
        var logs=new List<string>();var queue=new Queue<Action>();var provider=new Provider();
        var factory=new Factory{Provider=provider};
        var settings=new SdkConfig{LoginEnabled=false,ShareEnabled=false,LogCallback=(l,m)=>logs.Add(m)};
        SDK.InitializeWithFactory(settings,factory,a=>queue.Enqueue(a),(l,m)=>{});
        ShareResult result=null;var request=new ShareRequest("secret title","https://private","token=secret");
        SDK.Share(request,r=>result=r);Check(result.Error.Code=="MODULE.DISABLED"&&factory.Calls==0,"Disabled must not create or invoke");
        settings.ShareEnabled=true;SDK.InitializeWithFactory(settings,factory,a=>queue.Enqueue(a),(l,m)=>{});
        SDK.Share(null,r=>result=r);Check(result.Error.Code=="SHARE.INVALID_ARGUMENT","Invalid input");
        int delivered=0;SDK.Share(request,r=>{result=r;delivered++;});
        ShareResult busy=null;SDK.Share(request,r=>busy=r);Check(busy.Error.Code=="SHARE.IN_PROGRESS","Busy");
        provider.Callback(ShareResult.Dispatched());provider.Callback(ShareResult.Fail("late","late"));
        Check(delivered==0,"Must dispatch on supplied queue");queue.Dequeue()();
        Check(delivered==1&&result.Success&&result.CallId!=null,"One successful dispatch");
        SDK.Share(request,r=>result=r);SDK.CancelShare();provider.Callback(ShareResult.Dispatched());queue.Dequeue()();
        Check(result.Error.Code=="SHARE.CANCELLED"&&result.Error.CallId==result.CallId,"Local cancellation");
        SDK.Share(request,r=>result=r,1);Thread.Sleep(10);SDK.Tick();queue.Dequeue()();Check(result.Error.Code=="SHARE.TIMEOUT","Timeout");
        SDK.Share(request,r=>delivered++);provider.Callback(ShareResult.Dispatched());SDK.Shutdown();queue.Dequeue()();
        Check(delivered==1&&provider.Disposals==1,"Old completion suppressed and provider disposed");
        Check(!logs.Exists(x=>x.Contains("secret")||x.Contains("https://private")),"Share content redacted");
        Check(logs.Exists(x=>x.Contains("interface=SDK.Share")&&x.Contains("logId=")&&x.Contains("callId=")),"Structured logs");
        Console.WriteLine("PASS: share dispatch, factory switch, cancellation, timeout, stale results and privacy");
    }
}
