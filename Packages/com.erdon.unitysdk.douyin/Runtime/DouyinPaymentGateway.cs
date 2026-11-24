#if !ERDON_EXCLUDE_PAYMENTS || UNITY_EDITOR
using System;
using System.Globalization;
using System.Threading;
using TTSDK;
namespace ErDon.UnitySDK.Douyin {
    public enum DouyinPaymentRoute { Android, IosCustomerService }
    // Values must come from the authenticated game server catalog/order, not player input.
    public sealed class DouyinPaymentRequest {
        public string OrderId {get;private set;}
        public string ZoneId {get;private set;}
        public string ExtraInfo {get;private set;}
        public int BuyQuantity {get;private set;}
        public int AmountMinor {get;private set;}
        public string ProductName {get;private set;}
        public bool DirectPurchase {get;private set;}
        public DouyinPaymentRequest(string orderId,string zoneId,string extraInfo,int buyQuantity,int amountMinor=0,string productName=null,bool directPurchase=false){
            OrderId=orderId;ZoneId=zoneId;ExtraInfo=extraInfo;BuyQuantity=buyQuantity;AmountMinor=amountMinor;ProductName=productName;DirectPurchase=directPurchase;
        }
        public override string ToString(){return "DouyinPaymentRequest([REDACTED])";}
    }
    public sealed class DouyinPaymentGateway:IPlatformPaymentGateway<DouyinPaymentRequest> {
        readonly DouyinPaymentRoute route;
        readonly Action<RequestGamePaymentParam> android;
        readonly Action<OpenAwemeCustomerServiceParam> ios;
        readonly Func<bool> directSupported;
        bool disposed,busy;
        public DouyinPaymentGateway(DouyinPaymentRoute route):this(route,TT.RequestGamePayment,TT.OpenAwemeCustomerService,
            ()=>route==DouyinPaymentRoute.Android?CanIUse.RequestGamePaymentParams.GoodType:CanIUse.OpenAwemeCustomerServiceParams.GoodType){}
        internal DouyinPaymentGateway(DouyinPaymentRoute route,Action<RequestGamePaymentParam> android,Action<OpenAwemeCustomerServiceParam> ios,Func<bool> directSupported){
            if(!Enum.IsDefined(typeof(DouyinPaymentRoute),route)||android==null||ios==null||directSupported==null)throw new ArgumentException("Invalid payment gateway configuration");
            this.route=route;this.android=android;this.ios=ios;this.directSupported=directSupported;
        }
        public void Launch(DouyinPaymentRequest request,Action<PaymentLaunchResult> callback){
            if(callback==null)throw new ArgumentNullException("callback");
            if(disposed){callback(Fail("SDK.DISPOSED","Payment gateway disposed"));return;}
            if(!Valid(request)){callback(Fail("PAY.INVALID_REQUEST","Invalid server payment parameters"));return;}
            if(busy){callback(Fail("PAY.IN_PROGRESS","Payment panel is pending"));return;}
            busy=true;int completed=0;
            Action<PaymentLaunchResult> finish=result=>{if(disposed||Interlocked.Exchange(ref completed,1)!=0)return;busy=false;callback(result);};
            Action<ErrorInfo> fail=error=>finish(PaymentLaunchResult.Fail(new SdkError(error!=null&&error.ErrorCode==-2?"PAY.USER_CANCELLED":"PAY.PLATFORM_REJECTED",
                "Douyin payment flow did not confirm delivery; query game server",error==null?null:error.ErrorCode.ToString(CultureInfo.InvariantCulture),null)));
            try{
                if(request.DirectPurchase&&!directSupported()){finish(Fail("CAPABILITY.NOT_SUPPORTED","Direct purchase unavailable in this platform version"));return;}
                if(route==DouyinPaymentRoute.Android)android(new RequestGamePaymentParam{
                    Mode="game",Env=0,CurrencyType="CNY",Platform="android",CustomId=request.OrderId,ZoneId=request.ZoneId,ExtraInfo=request.ExtraInfo,
                    BuyQuantity=request.BuyQuantity,GoodType=request.DirectPurchase?2:0,OrderAmount=request.AmountMinor,GoodName=request.ProductName,
                    Success=result=>finish(PaymentLaunchResult.AwaitServerConfirmation()),Fail=fail
                });
                else ios(new OpenAwemeCustomerServiceParam{
                    CurrencyType="DIAMOND",CustomId=request.OrderId,ZoneId=request.ZoneId,ExtraInfo=request.ExtraInfo,
                    BuyQuantity=request.BuyQuantity,GoodType=request.DirectPurchase?2:0,OrderAmount=request.AmountMinor,GoodName=request.ProductName,
                    Success=result=>finish(PaymentLaunchResult.AwaitServerConfirmation()),Fail=fail
                });
            }catch{finish(Fail("PAY.LAUNCH_FAILED","Douyin payment invocation failed; query game server"));}
        }
        static bool Text(string value,int length){if(string.IsNullOrWhiteSpace(value)||value.Length>length||value.Trim()!=value)return false;foreach(char c in value)if(char.IsControl(c))return false;return true;}
        static bool Valid(DouyinPaymentRequest r){return r!=null&&Text(r.OrderId,256)&&Text(r.ZoneId,256)&&
            (r.ExtraInfo==null||r.ExtraInfo.Length<=256)&&
            (r.DirectPurchase?r.BuyQuantity==0&&r.AmountMinor>0&&Text(r.ProductName,10):r.BuyQuantity>0&&r.AmountMinor==0&&string.IsNullOrEmpty(r.ProductName));}
        static PaymentLaunchResult Fail(string code,string reason){return PaymentLaunchResult.Fail(new SdkError(code,reason));}
        public void Dispose(){disposed=true;}
    }
}
#endif
