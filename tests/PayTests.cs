using System;
using System.Collections.Generic;
using System.Threading;
using ErDon.UnitySDK;
class PayTests
{
    sealed class Provider : IPay, IPaymentWait {
        public Action<PaymentResult> Callback;
        public int Calls, Disposals, Ticks, Stops;
        public void Tick(){Ticks++;}
        public void CancelWait(){Stops++;}
        public void Pay(PaymentRequest request, Action<PaymentResult> callback) { Calls++; Callback=callback; }
        public void Dispose() { Disposals++; }
    }
    sealed class Factory : ISdkProviderFactory, IPayProviderFactory {
        public Provider Provider;
        public int Calls;
        public IAuth CreateAuth(Action<Action> post) { throw new Exception("disabled auth"); }
        public IStorage CreateStorage() { throw new Exception("disabled storage"); }
        public IPay CreatePay(Action<Action> post) { Calls++; return Provider; }
    }
    static void Check(bool ok,string reason) { if(!ok)throw new Exception(reason); }
    static void Main() {
        var logs=new List<string>();var queue=new Queue<Action>();var provider=new Provider();
        var factory=new Factory{Provider=provider};
        var settings=new SdkConfig{LoginEnabled=false,PayEnabled=false,LogCallback=(l,m)=>logs.Add(m)};
        SDK.InitializeWithFactory(settings,factory,a=>queue.Enqueue(a),(l,m)=>{});
        PaymentResult result=null;var request=new PaymentRequest("secret-session","secret-product","secret-request");
        SDK.Pay(request,r=>result=r);Check(result.Error.Code=="MODULE.DISABLED"&&factory.Calls==0,"Disabled must not create or invoke");
        settings.PayEnabled=true;SDK.InitializeWithFactory(settings,factory,a=>queue.Enqueue(a),(l,m)=>{});
        SDK.Pay(null,r=>result=r);Check(result.Error.Code=="PAY.INVALID_REQUEST","Invalid input");
        int delivered=0;SDK.Pay(request,r=>{result=r;delivered++;});
        SDK.Tick();Check(provider.Ticks==1,"SDK drives provider polling");
        PaymentResult busy=null;SDK.Pay(request,r=>busy=r);Check(busy.Error.Code=="PAY.IN_PROGRESS","Busy");
        provider.Callback(PaymentResult.FromOrder(OrderResult.Ok(new GameOrder(Guid.NewGuid().ToString("D"),"coins",99,"USD",OrderState.Fulfilled))));provider.Callback(PaymentResult.Fail("late","late"));
        Check(delivered==0,"Must dispatch on supplied queue");queue.Dequeue()();
        Check(delivered==1&&result.Success&&result.CallId!=null,"One successful dispatch");
        SDK.Pay(request,r=>result=r);SDK.CancelPay();provider.Callback(PaymentResult.FromOrder(OrderResult.Ok(new GameOrder(Guid.NewGuid().ToString("D"),"coins",99,"USD",OrderState.Fulfilled))));queue.Dequeue()();
        Check(result.Error.Code=="PAY.CANCELLED"&&result.Error.CallId==result.CallId,"Local cancellation");
        Check(provider.Stops==1,"Cancel stops provider wait");
        SDK.Pay(request,r=>result=r,1);Thread.Sleep(10);SDK.Tick();queue.Dequeue()();Check(result.Error.Code=="PAY.TIMEOUT","Timeout");
        Check(provider.Stops==2,"Timeout stops provider wait");
        SDK.Pay(request,r=>delivered++);provider.Callback(PaymentResult.FromOrder(OrderResult.Ok(new GameOrder(Guid.NewGuid().ToString("D"),"coins",99,"USD",OrderState.Fulfilled))));SDK.Shutdown();queue.Dequeue()();
        Check(delivered==1&&provider.Disposals==1,"Old completion suppressed and provider disposed");
        Check(!logs.Exists(x=>x.Contains("secret")||x.Contains("https://private")),"Pay content redacted");
        Check(logs.Exists(x=>x.Contains("interface=SDK.Pay")&&x.Contains("logId=")&&x.Contains("callId=")),"Structured logs");
        Console.WriteLine("PASS: pay dispatch, factory switch, cancellation, timeout, stale results and privacy");
    }
}
