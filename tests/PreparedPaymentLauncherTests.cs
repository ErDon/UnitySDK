using System;
using ErDon.UnitySDK;
class PreparedPaymentLauncherTests
{
    sealed class Preparation : IPrepaymentClient {
        public Action<PrepaymentResult> Callback; public int Calls,Disposals;
        public void Prepare(string id,Action<PrepaymentResult> callback){Calls++;Callback=callback;}
        public void Dispose(){Disposals++;}
    }
    sealed class Gateway : IPlatformPaymentGateway {
        public Action<PaymentLaunchResult> Callback; public string Id;public int Calls,Disposals;
        public void Launch(string id,Action<PaymentLaunchResult> callback){Id=id;Calls++;Callback=callback;}
        public void Dispose(){Disposals++;}
    }
    static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
    static void Main(){
        var prep=new Preparation();var gateway=new Gateway();var launcher=new PreparedPaymentLauncher(prep,gateway);
        var order=new GameOrder(Guid.NewGuid().ToString("D"),"coins",99,"USD",OrderState.Created);
        PaymentLaunchResult result=null;int callbacks=0;
        launcher.Launch(order,r=>{result=r;callbacks++;});
        launcher.Launch(order,r=>result=r);Check(result.Error.Code=="PAY.IN_PROGRESS"&&prep.Calls==1,"Concurrent preparation rejected");
        prep.Callback(PrepaymentResult.Ready("other","trade"));Check(result.Error.Code=="PAY.INVALID_RESPONSE"&&gateway.Calls==0,"Changed order refused");
        launcher.Launch(order,r=>{result=r;callbacks++;});
        prep.Callback(PrepaymentResult.Ready(order.OrderId,"trade"));prep.Callback(PrepaymentResult.Ready(order.OrderId,"second"));
        Check(gateway.Calls==1&&gateway.Id=="trade","One mapped launch");
        gateway.Callback(PaymentLaunchResult.AwaitServerConfirmation());gateway.Callback(null);
        Check(result.RequiresServerQuery&&callbacks==2,"First terminal result preserved");
        launcher.Launch(order,r=>result=r);prep.Callback(PrepaymentResult.Ready(order.OrderId,"bad\n"));
        Check(result.Error.Code=="PAY.INVALID_RESPONSE"&&gateway.Calls==1,"Malformed ID rejected");
        launcher.Launch(order,r=>callbacks++);launcher.Dispose();prep.Callback(PrepaymentResult.Ready(order.OrderId,"late"));
        Check(callbacks==2&&gateway.Calls==1&&prep.Disposals==1&&gateway.Disposals==1,"Owned dependencies disposed, late result ignored");
        launcher.Dispose();Check(prep.Disposals==1,"Dispose idempotent");
        string session=null;int creations=0;gateway=new Gateway();
        launcher=new PreparedPaymentLauncher(s=>{session=s;creations++;return prep=new Preparation();},gateway);
        launcher.Launch(order,r=>result=r);Check(result.Error.Code=="PAY.INVALID_REQUEST"&&creations==0,"Sessionless dynamic launch rejected");
        var alice=new PaymentRequest("alice-session","coins","one");
        launcher.Launch(alice,order,r=>result=r);Check(session=="alice-session"&&creations==1,"Current request supplies session");
        var old=prep;prep.Callback(PrepaymentResult.Ready(order.OrderId,"trade"));gateway.Callback(PaymentLaunchResult.AwaitServerConfirmation());
        Check(old.Disposals==1,"Per-request client released after terminal launch");
        launcher.Launch(new PaymentRequest("bob-session","coins","two"),order,r=>result=r);
        Check(session=="bob-session"&&creations==2&&!ReferenceEquals(old,prep),"Next account uses a fresh client");
        old.Callback(PrepaymentResult.Ready(order.OrderId,"late"));Check(gateway.Calls==1,"Old account callback cannot launch");
        launcher.Dispose();Check(prep.Disposals==1&&gateway.Disposals==1,"Active per-request client released on shutdown");
        Console.WriteLine("PREPARED_PAYMENT_LAUNCHER_TESTS_PASS");
    }
}
