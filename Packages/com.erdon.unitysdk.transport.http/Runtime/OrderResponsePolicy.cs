#if !ERDON_EXCLUDE_PAYMENTS || UNITY_EDITOR
using System;
using System.Globalization;
namespace ErDon.UnitySDK
{
    [Serializable] public sealed class OrderData { public string orderId, productId, currency, state; public long amountMinor; }
    [Serializable] public sealed class OrderResponseData { public OrderData order; public string errorCode, callId; }
    public static class OrderResponsePolicy
    {
        public static OrderResult Evaluate(long status, OrderResponseData data, string expectedOrderId = null, string expectedProductId = null, string retryAfter = null)
        {
            OrderResult result;var order=data==null?null:data.order;Guid id;OrderState state;
            if(status==200) {
                if(data==null || !string.IsNullOrEmpty(data.errorCode) || order==null || !Guid.TryParseExact(order.orderId,"D",out id) ||
                    string.IsNullOrWhiteSpace(order.productId) || order.amountMinor<=0 || order.amountMinor>9007199254740991L ||
                    order.currency==null || order.currency.Length!=3 || !AllCurrencyLetters(order.currency) ||
                    (order.state!="Created"&&order.state!="Paid"&&order.state!="Fulfilled") || !Enum.TryParse(order.state,out state) ||
                    (expectedOrderId!=null && !string.Equals(order.orderId,expectedOrderId,StringComparison.OrdinalIgnoreCase)) ||
                    (expectedProductId!=null && order.productId!=expectedProductId))
                    result=OrderResult.Fail("PAY.INVALID_RESPONSE","Invalid or mismatched order response");
                else result=OrderResult.Ok(new GameOrder(id.ToString("D"),order.productId,order.amountMinor,order.currency,state));
            } else {
                var code=data==null?null:data.errorCode;
                var error=new SdkError("PAY.SERVER_REJECTED","Order service rejected request");
                if(status==401&&code=="AUTH.SESSION_INVALID")error=new SdkError(code,"Game session expired or invalid").WithRetry(RetryPolicy.UserAction);
                else if(status==404&&(code=="PAY.ORDER_NOT_FOUND"||code=="PAY.PRODUCT_NOT_FOUND"))error=new SdkError(code,"Order or product is unavailable").WithRetry(RetryPolicy.Never);
                else if(status==409&&(code=="PAY.NOT_PAID"||code=="PAY.IDEMPOTENCY_CONFLICT"||code=="PAY.VERIFICATION_MISMATCH"||code=="PAY.TRANSACTION_REUSED"||code=="PAY.TRANSACTION_CONFLICT"))
                    error=new SdkError(code,"Order state or verified payment does not match the request").WithRetry(RetryPolicy.UserAction);
                else if(status==502&&code=="PAY.VERIFICATION_FAILED")error=new SdkError(code,"Platform payment verification could not complete").WithRetry(RetryPolicy.Delayed);
                else if(status==429) {
                    int seconds;int? delay=int.TryParse(retryAfter,NumberStyles.None,CultureInfo.InvariantCulture,out seconds)&&seconds>0?(int?)seconds:null;
                    error=new SdkError("PAY.RATE_LIMITED","Order requests are rate limited").WithRetry(RetryPolicy.Delayed,delay);
                }
                result=OrderResult.Fail(error);
            }
            return result.WithServerCallId(data==null?null:data.callId);
        }
        static bool AllCurrencyLetters(string value) { foreach(char c in value)if(c<'A'||c>'Z')return false;return true; }
    }
}

#endif
