using System;
using System.Threading;
namespace ErDon.UnitySDK
{
    public sealed class PrepaymentResult
    {
        public string OrderId { get; private set; }
        public string TradeOrderId { get; private set; }
        public SdkError Error { get; private set; }
        public string ServerCallId { get; private set; }
        public PrepaymentResult WithServerCallId(string value){Guid id;ServerCallId=Guid.TryParseExact(value,"D",out id)?id.ToString("D"):null;if(Error!=null)Error.ServerCallId=ServerCallId;return this;}
        public static PrepaymentResult Ready(string orderId, string tradeOrderId) { return new PrepaymentResult { OrderId=orderId, TradeOrderId=tradeOrderId }; }
        public static PrepaymentResult Fail(SdkError error) { if(error==null)throw new ArgumentNullException("error");return new PrepaymentResult { Error=error }; }
        public override string ToString() { return "PrepaymentResult([REDACTED])"; }
    }
    // Session-bound server client. Both invocation and completion must run on the main thread.
    public interface IPrepaymentClient : IDisposable { void Prepare(string orderId, Action<PrepaymentResult> callback); }
    public sealed class PreparedRequest<T> {
        public string OrderId {get;private set;}
        public T Value {get;private set;}
        public SdkError Error {get;private set;}
        public string ServerCallId {get;private set;}
        public static PreparedRequest<T> Ready(string orderId,T value){return new PreparedRequest<T>{OrderId=orderId,Value=value};}
        public static PreparedRequest<T> Fail(SdkError error){if(error==null)throw new ArgumentNullException("error");return new PreparedRequest<T>{Error=error};}
        public PreparedRequest<T> WithServerCallId(string value){Guid id;ServerCallId=Guid.TryParseExact(value,"D",out id)?id.ToString("D"):null;if(Error!=null)Error.ServerCallId=ServerCallId;return this;}
        public override string ToString(){return "PreparedRequest([REDACTED])";}
    }
    public interface IPreparedRequestClient<T>:IDisposable {void Prepare(string orderId,Action<PreparedRequest<T>> callback);}
#if !ERDON_EXCLUDE_PAYMENTS || UNITY_EDITOR
    // Typed platform payloads share the same aggregate payment and ownership lifecycle.
    public sealed class PreparedPaymentLauncher<T>:ISessionPaymentLauncher {
        readonly Func<string,IPreparedRequestClient<T>> createClient;
        readonly IPlatformPaymentGateway<T> gateway;
        readonly Func<GameOrder,T,bool> matchesOrder;
        IPreparedRequestClient<T> active;
        bool disposed,busy;
        public PreparedPaymentLauncher(Func<string,IPreparedRequestClient<T>> createClient,IPlatformPaymentGateway<T> gateway,Func<GameOrder,T,bool> matchesOrder){
            if(createClient==null||gateway==null||matchesOrder==null)throw new ArgumentNullException();
            this.createClient=createClient;this.gateway=gateway;this.matchesOrder=matchesOrder;
        }
        public void Launch(GameOrder order,Action<PaymentLaunchResult> callback){Launch(null,order,callback);}
        public void Launch(PaymentRequest request,GameOrder order,Action<PaymentLaunchResult> callback){
            if(callback==null)throw new ArgumentNullException("callback");
            if(disposed){callback(Fail("SDK.DISPOSED","Prepared launcher was disposed"));return;}
            Guid id;
            if(request==null||string.IsNullOrWhiteSpace(request.Session)||order==null||request.ProductId!=order.ProductId||
                !Guid.TryParseExact(order.OrderId,"D",out id)||order.State!=OrderState.Created){callback(Fail("PAY.INVALID_REQUEST","Matching session and created order required"));return;}
            if(busy){callback(Fail("PAY.IN_PROGRESS","Payment preparation or platform flow is pending"));return;}
            busy=true;int completed=0,prepared=0;
            Action<PaymentLaunchResult> finish=result=>{
                if(disposed||Interlocked.Exchange(ref completed,1)!=0)return;
                Release();busy=false;callback(result??Fail("PAY.INVALID_RESPONSE","Platform returned no result"));
            };
            Action<PreparedRequest<T>> ready=result=>{
                if(disposed||Interlocked.Exchange(ref prepared,1)!=0)return;
                if(result==null){finish(Fail("PAY.INVALID_RESPONSE","Missing preparation response"));return;}
                if(result.Error!=null){finish(PaymentLaunchResult.Fail(result.Error));return;}
                bool matches=false;
                try{matches=result.OrderId==order.OrderId&&matchesOrder(order,result.Value);}catch{}
                if(!matches){finish(Fail("PAY.INVALID_RESPONSE","Prepared parameters do not match order"));return;}
                try{gateway.Launch(result.Value,finish);}catch{finish(Fail("PAY.LAUNCH_FAILED","Platform gateway invocation failed"));}
            };
            try{
                var client=createClient(request.Session);
                if(disposed){try{if(client!=null)client.Dispose();}catch{}return;}
                if(client==null){finish(Fail("PAY.PREPARATION_UNAVAILABLE","Preparation factory returned no client"));return;}
                active=client;client.Prepare(order.OrderId,ready);
            }catch{ready(PreparedRequest<T>.Fail(new SdkError("PAY.PREPAYMENT_UNCERTAIN","Preparation failed; preserve order for reconciliation")));}
        }
        void Release(){var client=active;active=null;if(client!=null)try{client.Dispose();}catch{}}
        static PaymentLaunchResult Fail(string code,string reason){return PaymentLaunchResult.Fail(new SdkError(code,reason));}
        public void Dispose(){if(disposed)return;disposed=true;Release();gateway.Dispose();}
    }
    public sealed class PreparedPaymentLauncher : ISessionPaymentLauncher
    {
        readonly IPrepaymentClient preparation; readonly IPlatformPaymentGateway gateway;
        readonly Func<string,IPrepaymentClient> createPreparation;
        IPrepaymentClient activePreparation;
        bool disposed, busy;
        public PreparedPaymentLauncher(IPrepaymentClient preparation, IPlatformPaymentGateway gateway) {
            if(preparation==null||gateway==null)throw new ArgumentNullException();
            this.preparation=preparation;this.gateway=gateway;
        }
        public PreparedPaymentLauncher(Func<string,IPrepaymentClient> createPreparation,IPlatformPaymentGateway gateway) {
            if(createPreparation==null||gateway==null)throw new ArgumentNullException();
            this.createPreparation=createPreparation;this.gateway=gateway;
        }
        public void Launch(GameOrder order, Action<PaymentLaunchResult> callback) {
            Launch(null,order,callback);
        }
        public void Launch(PaymentRequest request, GameOrder order, Action<PaymentLaunchResult> callback) {
            if(callback==null)throw new ArgumentNullException("callback");
            if(disposed){callback(Fail("SDK.DISPOSED","Prepared launcher was disposed"));return;}
            if(createPreparation!=null&&(request==null||string.IsNullOrWhiteSpace(request.Session)||(order==null||request.ProductId!=order.ProductId))) {
                callback(Fail("PAY.INVALID_REQUEST","Matching payment request and game session are required"));return;
            }
            Guid id;
            if(order==null||!Guid.TryParseExact(order.OrderId,"D",out id)||order.State!=OrderState.Created){callback(Fail("PAY.INVALID_REQUEST","A created game order is required"));return;}
            if(busy){callback(Fail("PAY.IN_PROGRESS","Payment preparation or platform flow is pending"));return;}
            busy=true;int prepared=0,completed=0;
            Action<PaymentLaunchResult> finish=result=>{
                if(disposed||Interlocked.Exchange(ref completed,1)!=0)return;
                ReleasePreparation();busy=false;callback(result??Fail("PAY.INVALID_RESPONSE","Platform returned no result"));
            };
            Action<PrepaymentResult> ready=result=>{
                if(disposed||Interlocked.Exchange(ref prepared,1)!=0)return;
                if(result==null){finish(Fail("PAY.INVALID_RESPONSE","Missing prepayment response"));return;}
                if(result.Error!=null){finish(PaymentLaunchResult.Fail(result.Error));return;}
                if(result.OrderId!=order.OrderId||string.IsNullOrWhiteSpace(result.TradeOrderId)||result.TradeOrderId.Length>256||result.TradeOrderId.Trim()!=result.TradeOrderId||HasControl(result.TradeOrderId)){
                    finish(Fail("PAY.INVALID_RESPONSE","Prepayment response did not match the game order"));return;
                }
                try{gateway.Launch(result.TradeOrderId,finish);}catch{finish(Fail("PAY.LAUNCH_FAILED","Platform gateway invocation failed"));}
            };
            try{
                var client=preparation;
                if(createPreparation!=null) {
                    client=createPreparation(request.Session);
                    if(disposed){try{if(client!=null)client.Dispose();}catch{}return;}
                    if(client==null){finish(Fail("PAY.PREPARATION_UNAVAILABLE","Prepayment factory returned no client"));return;}
                    activePreparation=client;
                }
                client.Prepare(order.OrderId,ready);
            }catch{ready(PrepaymentResult.Fail(new SdkError("PAY.PREPAYMENT_UNCERTAIN","Prepayment invocation failed; preserve order for reconciliation")));}
        }
        void ReleasePreparation(){var client=activePreparation;activePreparation=null;if(client!=null)try{client.Dispose();}catch{}}
        static PaymentLaunchResult Fail(string code,string reason){return PaymentLaunchResult.Fail(new SdkError(code,reason));}
        static bool HasControl(string value){foreach(char item in value)if(char.IsControl(item))return true;return false;}
        public void Dispose(){if(disposed)return;disposed=true;ReleasePreparation();try{if(preparation!=null)preparation.Dispose();}finally{if(!ReferenceEquals(preparation,gateway))gateway.Dispose();}}
    }
#endif
}
