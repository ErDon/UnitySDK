#if !ERDON_EXCLUDE_PAYMENTS || UNITY_EDITOR
using System;
using System.Globalization;
using System.Threading;
using WeChatWASM;
namespace ErDon.UnitySDK.WeChat {
    // Opaque signed payload from the game server. Never reserialize SignData.
    public sealed class WeChatPaymentRequest {
        public string SignData {get;private set;}
        public string PaySignature {get;private set;}
        public string SessionSignature {get;private set;}
        public WeChatPaymentRequest(string signData,string paySignature,string sessionSignature){
            SignData=signData;PaySignature=paySignature;SessionSignature=sessionSignature;
        }
        public override string ToString(){return "WeChatPaymentRequest([REDACTED])";}
    }
    public sealed class WeChatPaymentGateway:IPlatformPaymentGateway<WeChatPaymentRequest> {
        readonly Action<RequestMidasPaymentGameItemOption> dispatch;
        bool disposed,busy;
        public WeChatPaymentGateway():this(WX.RequestMidasPaymentGameItem){}
        internal WeChatPaymentGateway(Action<RequestMidasPaymentGameItemOption> dispatch){
            if(dispatch==null)throw new ArgumentNullException("dispatch");this.dispatch=dispatch;
        }
        public void Launch(WeChatPaymentRequest request,Action<PaymentLaunchResult> callback){
            if(callback==null)throw new ArgumentNullException("callback");
            if(disposed){callback(Fail("SDK.DISPOSED","WeChat payment gateway disposed"));return;}
            if(request==null||string.IsNullOrWhiteSpace(request.SignData)||request.SignData.Length>8192||
                !Digest(request.PaySignature)||!Signature(request.SessionSignature)){
                callback(Fail("PAY.INVALID_REQUEST","Signed game payment parameters are required"));return;
            }
            if(busy){callback(Fail("PAY.IN_PROGRESS","Payment panel is pending"));return;}
            busy=true;int completed=0;
            Action<PaymentLaunchResult> finish=result=>{if(disposed||Interlocked.Exchange(ref completed,1)!=0)return;busy=false;callback(result);};
            Action<MidasPaymentError> rejected=error=>finish(PaymentLaunchResult.Fail(new SdkError("PAY.PLATFORM_REJECTED",
                "WeChat payment did not confirm delivery; query game server",Code(error),null)));
            try{
                dispatch(new RequestMidasPaymentGameItemOption{
                    signData=request.SignData,paySig=request.PaySignature,signature=request.SessionSignature,
                    success=result=>{
                        if(result!=null&&result.errCode==0)finish(PaymentLaunchResult.AwaitServerConfirmation());
                        else rejected(result);
                    },fail=rejected
                });
            }catch{finish(Fail("PAY.LAUNCH_FAILED","WeChat payment invocation failed; query game server"));}
        }
        static string Code(MidasPaymentError error){return error==null||double.IsNaN(error.errCode)||double.IsInfinity(error.errCode)?null:error.errCode.ToString(CultureInfo.InvariantCulture);}
        static bool Digest(string value){if(value==null||value.Length!=64)return false;foreach(char c in value)if(!((c>='0'&&c<='9')||(c>='a'&&c<='f')||(c>='A'&&c<='F')))return false;return true;}
        static bool Signature(string value){if(string.IsNullOrEmpty(value)||value.Length>256)return false;foreach(char c in value)if(c<'!'||c>'~')return false;return true;}
        static PaymentLaunchResult Fail(string code,string reason){return PaymentLaunchResult.Fail(new SdkError(code,reason));}
        public void Dispose(){disposed=true;}
    }
}
#endif
