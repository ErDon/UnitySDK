using System;
using System.Threading;
namespace ErDon.UnitySDK
{
    public sealed class PrepaymentResult
    {
        public string OrderId { get; private set; }
        public string TradeOrderId { get; private set; }
        public SdkError Error { get; private set; }
        public static PrepaymentResult Ready(string orderId, string tradeOrderId) { return new PrepaymentResult { OrderId=orderId, TradeOrderId=tradeOrderId }; }
        public static PrepaymentResult Fail(SdkError error) { if(error==null)throw new ArgumentNullException("error");return new PrepaymentResult { Error=error }; }
        public override string ToString() { return "PrepaymentResult([REDACTED])"; }
    }
    // Session-bound server client. Both invocation and completion must run on the main thread.
    public interface IPrepaymentClient : IDisposable { void Prepare(string orderId, Action<PrepaymentResult> callback); }
#if !ERDON_EXCLUDE_PAYMENTS || UNITY_EDITOR
    public sealed class PreparedPaymentLauncher : IPaymentLauncher
    {
        readonly IPrepaymentClient preparation; readonly IPlatformPaymentGateway gateway;
        bool disposed, busy;
        public PreparedPaymentLauncher(IPrepaymentClient preparation, IPlatformPaymentGateway gateway) {
            if(preparation==null||gateway==null)throw new ArgumentNullException();
            this.preparation=preparation;this.gateway=gateway;
        }
        public void Launch(GameOrder order, Action<PaymentLaunchResult> callback) {
            if(callback==null)throw new ArgumentNullException("callback");
            if(disposed){callback(Fail("SDK.DISPOSED","Prepared launcher was disposed"));return;}
            Guid id;
            if(order==null||!Guid.TryParseExact(order.OrderId,"D",out id)||order.State!=OrderState.Created){callback(Fail("PAY.INVALID_REQUEST","A created game order is required"));return;}
            if(busy){callback(Fail("PAY.IN_PROGRESS","Payment preparation or platform flow is pending"));return;}
            busy=true;int prepared=0,completed=0;
            Action<PaymentLaunchResult> finish=result=>{
                if(disposed||Interlocked.Exchange(ref completed,1)!=0)return;
                busy=false;callback(result??Fail("PAY.INVALID_RESPONSE","Platform returned no result"));
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
            try{preparation.Prepare(order.OrderId,ready);}catch{ready(PrepaymentResult.Fail(new SdkError("PAY.PREPAYMENT_UNCERTAIN","Prepayment invocation failed; preserve order for reconciliation")));}
        }
        static PaymentLaunchResult Fail(string code,string reason){return PaymentLaunchResult.Fail(new SdkError(code,reason));}
        static bool HasControl(string value){foreach(char item in value)if(char.IsControl(item))return true;return false;}
        public void Dispose(){if(disposed)return;disposed=true;try{preparation.Dispose();}finally{if(!ReferenceEquals(preparation,gateway))gateway.Dispose();}}
    }
#endif
}
