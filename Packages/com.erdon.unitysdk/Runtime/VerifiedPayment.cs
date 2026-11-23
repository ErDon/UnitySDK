using System;
using System.Threading;
namespace ErDon.UnitySDK
{
    public sealed class PaymentRequest
    {
        public string Session { get; private set; }
        public string ProductId { get; private set; }
        public string RequestId { get; private set; }
        public PaymentRequest(string session,string productId,string requestId) { Session=session;ProductId=productId;RequestId=requestId; }
        public override string ToString() { return "PaymentRequest([REDACTED])"; }
    }
    public sealed class PaymentResult
    {
        public bool Success { get { return Error==null && Order!=null && Order.State==OrderState.Fulfilled; } }
        public GameOrder Order { get; private set; }
        public SdkError Error { get; private set; }
        public string ServerCallId { get; private set; }
        public static PaymentResult FromOrder(OrderResult result)
        {
            if(result==null)return Fail("PAY.INVALID_RESPONSE","Missing order response");
            if(!result.Success)return new PaymentResult{Error=result.Error,ServerCallId=result.ServerCallId};
            var order=result.Order;
            if(order==null)return Fail("PAY.INVALID_RESPONSE","Missing order");
            var payment=new PaymentResult{Order=order,ServerCallId=result.ServerCallId,Error=order.State==OrderState.Fulfilled?null:
                new SdkError(order.State==OrderState.Paid?"PAY.FULFILLMENT_PENDING":"PAY.NOT_PAID",
                    order.State==OrderState.Paid?"Payment verified; server delivery is pending":"Server has not verified payment")};
            if(payment.Error!=null)payment.Error.ServerCallId=result.ServerCallId;
            return payment;
        }
        public static PaymentResult Fail(string code,string reason) { return new PaymentResult{Error=new SdkError(code,reason)}; }
        public static PaymentResult Fail(SdkError error,GameOrder order=null) { if(error==null)throw new ArgumentNullException("error");return new PaymentResult{Error=error,Order=order}; }
    }
    public sealed class PaymentLaunchResult
    {
        public string Evidence { get; private set; }
        public SdkError Error { get; private set; }
        public static PaymentLaunchResult Submitted(string evidence) { return new PaymentLaunchResult{Evidence=evidence}; }
        public static PaymentLaunchResult Fail(SdkError error) { if(error==null)throw new ArgumentNullException("error");return new PaymentLaunchResult{Error=error}; }
        public override string ToString() { return "PaymentLaunchResult([REDACTED])"; }
    }
    public interface IPaymentLauncher : IDisposable { void Launch(GameOrder order,Action<PaymentLaunchResult> callback); }
    public interface IPay : IDisposable { void Pay(PaymentRequest request,Action<PaymentResult> callback); }

    // Owns order client and launcher. Platform launch success never authorizes entitlement delivery.
    public sealed class VerifiedPayment : IPay
    {
        readonly IOrderClient orders;readonly IPaymentLauncher launcher;readonly Action<Action> post;
        bool disposed,busy;
        public VerifiedPayment(IOrderClient orders,IPaymentLauncher launcher,Action<Action> mainThreadPost)
        {
            if(orders==null||launcher==null||mainThreadPost==null)throw new ArgumentNullException();
            this.orders=orders;this.launcher=launcher;post=mainThreadPost;
        }
        public void Pay(PaymentRequest request,Action<PaymentResult> callback)
        {
            if(callback==null)throw new ArgumentNullException("callback");
            if(disposed){callback(PaymentResult.Fail("SDK.DISPOSED","Payment provider disposed"));return;}
            if(request==null||string.IsNullOrWhiteSpace(request.Session)||string.IsNullOrWhiteSpace(request.ProductId)||string.IsNullOrWhiteSpace(request.RequestId)) {
                callback(PaymentResult.Fail("PAY.INVALID_REQUEST","Session, product and stable request identifier are required"));return;
            }
            if(busy){callback(PaymentResult.Fail("PAY.IN_PROGRESS","Another payment is still in progress"));return;}
            busy=true;int completed=0,created=0,launched=0,verified=0;
            Action<PaymentResult> finish=result=>{
                if(disposed||Interlocked.Exchange(ref completed,1)!=0)return;
                busy=false;callback(result);
            };
            Action<OrderResult> onCreated=result=>{
                if(Interlocked.Exchange(ref created,1)!=0)return;
                post(()=>{
                    if(disposed)return;
                    if(result==null||!result.Success){finish(PaymentResult.FromOrder(result));return;}
                    var order=result.Order;
                    if(!ValidOrder(order,request.ProductId)){finish(PaymentResult.Fail("PAY.INVALID_RESPONSE","Invalid created order"));return;}
                    if(order.State!=OrderState.Created){finish(PaymentResult.FromOrder(result));return;}
                    Action<PaymentLaunchResult> onLaunched=launch=>{
                        if(Interlocked.Exchange(ref launched,1)!=0)return;
                        post(()=>{
                            if(disposed)return;
                            Action<OrderResult> onVerified=confirmation=>{
                                if(Interlocked.Exchange(ref verified,1)!=0)return;
                                post(()=>{
                                    if(disposed)return;
                                    if(confirmation!=null&&confirmation.Success) {
                                        var current=confirmation.Order;
                                        if(!ValidOrder(current,order.ProductId)||current.OrderId!=order.OrderId||current.AmountMinor!=order.AmountMinor||current.Currency!=order.Currency) {
                                            finish(PaymentResult.Fail("PAY.INVALID_RESPONSE","Payment confirmation changed the order"));return;
                                        }
                                        if(current.State==OrderState.Created&&launch!=null&&launch.Error!=null) {
                                            finish(PaymentResult.Fail(launch.Error,current));return;
                                        }
                                    }
                                    finish(PaymentResult.FromOrder(confirmation));
                                });
                            };
                            try {
                                // Even cancellation/failure may race with a completed payment: query before reporting it.
                                if(launch==null||launch.Error!=null)orders.Get(request.Session,order.OrderId,onVerified);
                                else orders.Confirm(request.Session,order.OrderId,launch.Evidence,onVerified);
                            }catch{onVerified(OrderResult.Fail("PAY.VERIFICATION_FAILED","Order verification failed"));}
                        });
                    };
                    try{launcher.Launch(order,onLaunched);}catch{onLaunched(PaymentLaunchResult.Fail(new SdkError("PAY.LAUNCH_FAILED","Platform payment invocation failed")));}
                });
            };
            try{orders.Create(request.Session,request.ProductId,request.RequestId,onCreated);}catch{onCreated(OrderResult.Fail("PAY.CREATE_FAILED","Order creation failed"));}
        }
        static bool ValidOrder(GameOrder order,string product)
        {
            Guid id;return order!=null&&Guid.TryParseExact(order.OrderId,"D",out id)&&order.ProductId==product&&order.AmountMinor>0&&
                order.AmountMinor<=9007199254740991L&&!string.IsNullOrWhiteSpace(order.Currency)&&Enum.IsDefined(typeof(OrderState),order.State);
        }
        public void Dispose(){if(disposed)return;disposed=true;try{orders.Dispose();}finally{if(!ReferenceEquals(orders,launcher))launcher.Dispose();}}
    }
}
