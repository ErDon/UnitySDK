#if !ERDON_EXCLUDE_PAYMENTS || UNITY_EDITOR
using System;
namespace ErDon.UnitySDK
{
    [Serializable] public sealed class PrepaymentData { public string orderId, tradeOrderId; }
    [Serializable] public sealed class PrepaymentResponseData { public PrepaymentData prepayment; public string errorCode,callId; }
    public static class PrepaymentResponsePolicy
    {
        public static PrepaymentResult Evaluate(long status,PrepaymentResponseData data,string expectedOrderId,string retryAfter=null)
        {
            Guid expected,actual;
            if(status==200){
                var value=data==null?null:data.prepayment;
                if(data==null||!string.IsNullOrEmpty(data.errorCode)||value==null||!Guid.TryParseExact(expectedOrderId,"D",out expected)||
                    !Guid.TryParseExact(value.orderId,"D",out actual)||actual!=expected||!ValidTrade(value.tradeOrderId))
                    return PrepaymentResult.Fail(new SdkError("PAY.INVALID_RESPONSE","Invalid or mismatched prepayment response"));
                return PrepaymentResult.Ready(expected.ToString("D"),value.tradeOrderId).WithServerCallId(data.callId);
            }
            var code=data==null?null:data.errorCode;
            SdkError error;
            if((status==409||status==502)&&code=="PAY.PREPAYMENT_UNCERTAIN")
                error=new SdkError(code,"Prepayment is unresolved; preserve the order for reconciliation").WithRetry(RetryPolicy.UserAction);
            else if(status==409&&(code=="PAY.PREPAYMENT_CONFLICT"||code=="PAY.ORDER_ALREADY_PAID"))
                error=new SdkError(code,"Prepayment or order state conflicts with this request").WithRetry(RetryPolicy.UserAction);
            else if(status==503&&code=="PAY.CREDENTIAL_UNAVAILABLE")
                error=new SdkError(code,"Server platform credential is unavailable; re-authentication may be required").WithRetry(RetryPolicy.UserAction);
            else if(status==401&&code=="PAY.PLATFORM_TOKEN_REJECTED")
                error=new SdkError(code,"Platform token was rejected; check server order before retrying").WithRetry(RetryPolicy.UserAction);
            else error=OrderResponsePolicy.Evaluate(status,new OrderResponseData{errorCode=code,callId=data==null?null:data.callId},retryAfter:retryAfter).Error;
            return PrepaymentResult.Fail(error).WithServerCallId(data==null?null:data.callId);
        }
        static bool ValidTrade(string value){if(string.IsNullOrWhiteSpace(value)||value.Length>256||value.Trim()!=value)return false;foreach(char c in value)if(char.IsControl(c))return false;return true;}
    }
}
#endif
