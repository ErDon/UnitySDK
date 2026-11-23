using System;
using System.Collections.Generic;
using ErDon.UnitySDK;
class PaymentPipelineTests
{
    sealed class Orders : IOrderClient {
        public Action<OrderResult> Created,Confirmed,Queried;
        public int Creates,Confirms,Gets,Disposals;
        public string RequestId;
        public void Create(string s,string p,string r,Action<OrderResult> callback){Creates++;RequestId=r;Created=callback;}
        public void Confirm(string s,string id,string evidence,Action<OrderResult> callback){Confirms++;Confirmed=callback;}
        public void Get(string s,string id,Action<OrderResult> callback){Gets++;Queried=callback;}
        public void Dispose(){Disposals++;}
    }
    sealed class Launcher : IPaymentLauncher {
        public int Calls,Disposals;public Action<PaymentLaunchResult> Callback;
        public void Launch(GameOrder order,Action<PaymentLaunchResult> callback){Calls++;Callback=callback;}
        public void Dispose(){Disposals++;}
    }
    static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
    static readonly string Id=Guid.NewGuid().ToString("D");
    static OrderResult Order(OrderState state,string id=null){return OrderResult.Ok(new GameOrder(id??Id,"coins",99,"USD",state));}
    static void Main(){
        var request=new PaymentRequest("secret-session","coins","stable-id");
        var queue=new Queue<Action>();var orders=new Orders();var launcher=new Launcher();
        var pay=new VerifiedPayment(orders,launcher,a=>queue.Enqueue(a));PaymentResult result=null;int callbacks=0;
        Action drain=()=>{while(queue.Count>0)queue.Dequeue()();};
        pay.Pay(request,r=>{result=r;callbacks++;});
        PaymentResult busy=null;pay.Pay(request,r=>busy=r);Check(busy.Error.Code=="PAY.IN_PROGRESS","Concurrent launch rejected");
        orders.Created(Order(OrderState.Created));orders.Created(Order(OrderState.Created));Check(launcher.Calls==0,"Must queue to main thread");drain();
        Check(launcher.Calls==1&&orders.RequestId=="stable-id","One launch and stable id");
        launcher.Callback(PaymentLaunchResult.Submitted("secret-receipt"));launcher.Callback(PaymentLaunchResult.Submitted("late"));drain();
        Check(orders.Confirms==1&&result==null,"Platform success waits for backend");
        orders.Confirmed(Order(OrderState.Paid));drain();Check(!result.Success&&result.Error.Code=="PAY.FULFILLMENT_PENDING","Paid is not delivered");
        pay.Pay(request,r=>result=r);orders.Created(Order(OrderState.Paid));drain();Check(launcher.Calls==1,"Paid retry must not launch cashier");
        pay.Pay(request,r=>result=r);orders.Created(Order(OrderState.Fulfilled));drain();Check(result.Success&&launcher.Calls==1,"Fulfilled retry returns success");
        pay.Pay(request,r=>result=r);orders.Created(Order(OrderState.Created));drain();
        launcher.Callback(PaymentLaunchResult.Fail(new SdkError("PAY.USER_CANCELLED","Cancelled")));drain();
        Check(orders.Gets==1,"Cancellation reconciles server status");orders.Queried(Order(OrderState.Fulfilled));drain();Check(result.Success,"Server completed payment wins cancellation race");
        pay.Pay(request,r=>result=r);orders.Created(Order(OrderState.Created));drain();launcher.Callback(PaymentLaunchResult.Submitted("receipt"));drain();
        orders.Confirmed(Order(OrderState.Fulfilled,Guid.NewGuid().ToString("D")));drain();Check(result.Error.Code=="PAY.INVALID_RESPONSE","Switched order rejected");
        pay.Pay(request,r=>callbacks++);orders.Created(Order(OrderState.Created));pay.Dispose();drain();
        Check(callbacks==1&&orders.Disposals==1&&launcher.Disposals==1,"Disposed pipeline ignores queued callbacks");
        Check(!request.ToString().Contains("secret")&&!PaymentLaunchResult.Submitted("secret").ToString().Contains("secret"),"Sensitive data not formatted");
        Console.WriteLine("PASS: payment verification, idempotent recovery, cancellation race and ownership");
    }
}
