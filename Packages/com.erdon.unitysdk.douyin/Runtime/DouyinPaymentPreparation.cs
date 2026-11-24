#if !ERDON_EXCLUDE_PAYMENTS || UNITY_EDITOR
using System;
namespace ErDon.UnitySDK.Douyin {
    [Serializable] public sealed class DouyinPrepaymentData {
        public string orderId,route,zoneId,extraInfo,productName;
        public int amountMinor,buyQuantity;
        public bool directPurchase;
    }
    [Serializable] public sealed class DouyinPrepaymentResponse {
        public DouyinPrepaymentData prepayment;
        public string errorCode,callId;
    }
    public static class DouyinPaymentPreparation {
        public static PreparedRequest<DouyinPaymentRequest> Evaluate(DouyinPrepaymentResponse data,DouyinPaymentRoute route){
            var value=data==null?null:data.prepayment;Guid id;
            if(!Enum.IsDefined(typeof(DouyinPaymentRoute),route)||data==null||!string.IsNullOrEmpty(data.errorCode)||value==null||
                !Guid.TryParseExact(value.orderId,"D",out id)||value.route!=route.ToString()||!Text(value.zoneId,256)||
                !Text(value.extraInfo,256)||!Text(value.productName,10)||!value.directPurchase||value.buyQuantity!=0||value.amountMinor<=0)
                return PreparedRequest<DouyinPaymentRequest>.Fail(new SdkError("PAY.INVALID_RESPONSE","Invalid Douyin preparation response"));
            return PreparedRequest<DouyinPaymentRequest>.Ready(id.ToString("D"),new DouyinPaymentRequest(id.ToString("D"),value.zoneId,value.extraInfo,
                0,value.amountMinor,value.productName,true)).WithServerCallId(data.callId);
        }
        public static bool MatchesOrder(GameOrder order,DouyinPaymentRequest value){
            return order!=null&&value!=null&&value.OrderId==order.OrderId&&order.Currency=="CNY"&&value.AmountMinor==order.AmountMinor&&value.DirectPurchase;
        }
#if UNITY_5_3_OR_NEWER
        public static PreparedRequest<DouyinPaymentRequest> Parse(string json,DouyinPaymentRoute route){
            return Evaluate(UnityEngine.JsonUtility.FromJson<DouyinPrepaymentResponse>(json),route);
        }
#endif
        static bool Text(string value,int max){if(string.IsNullOrWhiteSpace(value)||value.Trim()!=value||value.Length>max)return false;foreach(char c in value)if(char.IsControl(c))return false;return true;}
    }
}
#endif
