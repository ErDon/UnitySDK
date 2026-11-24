using System;
using System.Threading;
namespace ErDon.UnitySDK
{
    public sealed class PaymentRequest
    {
        public string Session { get; private set; }
        public string ProductId { get; private set; }
        /// <summary>同一次购买及其恢复必须复用此幂等键；换键表示新的购买意图。</summary>
        public string RequestId { get; private set; }
        public PaymentRequest(string session,string productId,string requestId) { Session=session;ProductId=productId;RequestId=requestId; }
        public override string ToString() { return "PaymentRequest([REDACTED])"; }
    }
    public sealed class PaymentResult
    {
        internal PaymentResult Snapshot() { var copy = (PaymentResult)MemberwiseClone(); copy.Error = Error == null ? null : Error.Snapshot(); return copy; }
        public string CallId { get; internal set; }
        public string TraceId { get { return CallId; } }
        // Paid 仅说明付款已核验；权益发放完成（Fulfilled）才是对游戏上层的成功。
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
        public bool RequiresServerQuery { get; private set; }
        public static PaymentLaunchResult AwaitServerConfirmation() { return new PaymentLaunchResult{RequiresServerQuery=true}; }
        public static PaymentLaunchResult Fail(SdkError error) { if(error==null)throw new ArgumentNullException("error");return new PaymentLaunchResult{Error=error}; }
        public override string ToString() { return "PaymentLaunchResult([REDACTED])"; }
    }
    public interface IPaymentLauncher : IDisposable { void Launch(GameOrder order,Action<PaymentLaunchResult> callback); }
    public interface ISessionPaymentLauncher : IPaymentLauncher { void Launch(PaymentRequest request,GameOrder order,Action<PaymentLaunchResult> callback); }
    // Engine/platform layer only. ID must come from a trusted server prepayment response.
    public interface IPlatformPaymentGateway<TRequest> : IDisposable { void Launch(TRequest request, Action<PaymentLaunchResult> callback); }
    public interface IPlatformPaymentGateway : IPlatformPaymentGateway<string> { }
    public interface IPay : IDisposable { void Pay(PaymentRequest request,Action<PaymentResult> callback); }
    // Optional main-thread lifecycle for bounded payment reconciliation.
    public interface IPaymentWait { void Tick(); void CancelWait(); }

    // Owns order client and launcher. Platform launch success never authorizes entitlement delivery.
#if !ERDON_EXCLUDE_PAYMENTS || UNITY_EDITOR
    public sealed class VerifiedPayment : IPay, IPaymentWait
    {
        readonly IOrderClient orders;readonly IPaymentLauncher launcher;readonly Action<Action> post;
        bool disposed,busy;
        readonly Func<double> clock; readonly int maxQueries,intervalMilliseconds;
        Action pendingQuery; double nextQuery; int operation;
        public VerifiedPayment(IOrderClient orders,IPaymentLauncher launcher,Action<Action> mainThreadPost,
            int maxQueries=12,int intervalMilliseconds=2000,Func<double> monotonicMilliseconds=null)
        {
            if(orders==null||launcher==null||mainThreadPost==null)throw new ArgumentNullException();
            if(maxQueries<1||maxQueries>60||intervalMilliseconds<100||intervalMilliseconds>60000)throw new ArgumentOutOfRangeException();
            this.orders=orders;this.launcher=launcher;post=mainThreadPost;
            this.maxQueries=maxQueries;this.intervalMilliseconds=intervalMilliseconds;
            clock=monotonicMilliseconds??(()=>System.Diagnostics.Stopwatch.GetTimestamp()*1000.0/System.Diagnostics.Stopwatch.Frequency);
        }
        public void Pay(PaymentRequest request,Action<PaymentResult> callback)
        {
            if(callback==null)throw new ArgumentNullException("callback");
            if(disposed){callback(PaymentResult.Fail("SDK.DISPOSED","Payment provider disposed"));return;}
            if(request==null||string.IsNullOrWhiteSpace(request.Session)||string.IsNullOrWhiteSpace(request.ProductId)||string.IsNullOrWhiteSpace(request.RequestId)) {
                callback(PaymentResult.Fail("PAY.INVALID_REQUEST","Session, product and stable request identifier are required"));return;
            }
            if(busy){callback(PaymentResult.Fail("PAY.IN_PROGRESS","Another payment is still in progress"));return;}
            // epoch 隔离取消前的回调；每阶段的原子标记拦截第三方重复回调。
            busy=true;int completed=0,created=0,launched=0,epoch=++operation;
            Action<PaymentResult> finish=result=>{
                if(disposed||epoch!=operation||Interlocked.Exchange(ref completed,1)!=0)return;
                busy=false;pendingQuery=null;callback(result);
            };
            Action<OrderResult> onCreated=result=>{
                if(Interlocked.Exchange(ref created,1)!=0)return;
                post(()=>{
                    if(disposed||epoch!=operation)return;
                    if(result==null||!result.Success){finish(PaymentResult.FromOrder(result));return;}
                    var order=result.Order;
                    if(!ValidOrder(order,request.ProductId)){finish(PaymentResult.Fail("PAY.INVALID_RESPONSE","Invalid created order"));return;}
                    // 幂等创建可能返回已付款/已发货订单，不能再次拉起收银台。
                    if(order.State!=OrderState.Created){finish(PaymentResult.FromOrder(result));return;}
                    Action<PaymentLaunchResult> onLaunched=launch=>{
                        if(Interlocked.Exchange(ref launched,1)!=0)return;
                        post(()=>{
                            if(disposed||epoch!=operation)return;
                            int attempts=0;
                            Action<bool> verify=null;
                            verify=confirm=>{
                            if(disposed||epoch!=operation||completed!=0)return;
                            attempts++;int verified=0;
                            Action<OrderResult> onVerified=confirmation=>{
                                if(Interlocked.Exchange(ref verified,1)!=0)return;
                                post(()=>{
                                    if(disposed||epoch!=operation||completed!=0)return;
                                    if(confirmation!=null&&confirmation.Success) {
                                        var current=confirmation.Order;
                                        if(!ValidOrder(current,order.ProductId)||current.OrderId!=order.OrderId||current.AmountMinor!=order.AmountMinor||current.Currency!=order.Currency) {
                                            finish(PaymentResult.Fail("PAY.INVALID_RESPONSE","Payment confirmation changed the order"));return;
                                        }
                                        if(current.State==OrderState.Created&&launch!=null&&launch.Error!=null) {
                                            finish(PaymentResult.Fail(launch.Error,current));return;
                                        }
                                        if(current.State!=OrderState.Fulfilled&&attempts<maxQueries) {
                                            nextQuery=clock()+intervalMilliseconds;
                                            pendingQuery=()=>verify(false);return;
                                        }
                                    }
                                    finish(PaymentResult.FromOrder(confirmation));
                                });
                            };
                            try {
                                // Even cancellation/failure may race with a completed payment: query before reporting it.
                                if(!confirm)orders.Get(request.Session,order.OrderId,onVerified);
                                else orders.Confirm(request.Session,order.OrderId,launch.Evidence,onVerified);
                            }catch{onVerified(OrderResult.Fail("PAY.VERIFICATION_FAILED","Order verification failed"));}
                            };
                            verify(launch!=null&&launch.Error==null&&!launch.RequiresServerQuery);
                        });
                    };
                    try{
                        var sessionLauncher=launcher as ISessionPaymentLauncher;
                        if(sessionLauncher!=null)sessionLauncher.Launch(request,order,onLaunched);
                        else launcher.Launch(order,onLaunched);
                    }catch{onLaunched(PaymentLaunchResult.Fail(new SdkError("PAY.LAUNCH_FAILED","Platform payment invocation failed")));}
                });
            };
            try{orders.Create(request.Session,request.ProductId,request.RequestId,onCreated);}catch{onCreated(OrderResult.Fail("PAY.CREATE_FAILED","Order creation failed"));}
        }
        static bool ValidOrder(GameOrder order,string product)
        {
            Guid id;return order!=null&&Guid.TryParseExact(order.OrderId,"D",out id)&&order.ProductId==product&&order.AmountMinor>0&&
                order.AmountMinor<=9007199254740991L&&!string.IsNullOrWhiteSpace(order.Currency)&&Enum.IsDefined(typeof(OrderState),order.State);
        }
        public void Tick(){if(disposed||pendingQuery==null||clock()<nextQuery)return;var query=pendingQuery;pendingQuery=null;query();}
        /// <summary>只停止本地查单和结果派发，不撤销交易、不退款；保留原购买幂等键以便恢复。</summary>
        public void CancelWait(){operation++;busy=false;pendingQuery=null;}
        public void Dispose(){if(disposed)return;disposed=true;CancelWait();try{orders.Dispose();}finally{if(!ReferenceEquals(orders,launcher))launcher.Dispose();}}
    }
#endif
}
