#if !ERDON_EXCLUDE_PAYMENTS || UNITY_EDITOR
using System;
namespace ErDon.UnitySDK.WeChat {
    [Serializable] public sealed class WeChatPrepaymentData {
        public string orderId,signData,paySignature,sessionSignature;
    }
    [Serializable] public sealed class WeChatPrepaymentResponse {
        public WeChatPrepaymentData prepayment;
        public string errorCode,callId;
    }
    [Serializable] public sealed class WeChatSignData {
        public string mode,offerId,currencyType,platform,zoneId,productId,outTradeNo,attach;
        public int buyQuantity,env,goodsPrice;
    }
    /// <summary>Validate server preparation without reserializing the signed JSON.</summary>
    public static class WeChatPaymentPreparation {
        public static PreparedRequest<WeChatPaymentRequest> Evaluate(WeChatPrepaymentResponse response,Func<string,WeChatSignData> parseSignData){
            var value=response==null?null:response.prepayment;Guid id;
            if(response==null||!string.IsNullOrEmpty(response.errorCode)||value==null||parseSignData==null||
                !Guid.TryParseExact(value.orderId,"D",out id)||!Text(value.signData,8192)||!Digest(value.paySignature)||!Digest(value.sessionSignature))return Invalid();
            WeChatSignData data;
            try{data=parseSignData(value.signData);}catch{return Invalid();}
            if(data==null||data.mode!="goods"||data.currencyType!="CNY"||data.platform!="android"||data.buyQuantity!=1||
                (data.env!=0&&data.env!=1)||data.goodsPrice<=0||!Text(data.offerId,256)||!Text(data.zoneId,256)||
                !Text(data.productId,256)||!Text(data.attach,256)||!PlatformOrder(data.outTradeNo))return Invalid();
            return PreparedRequest<WeChatPaymentRequest>.Ready(id.ToString("D"),new WeChatPaymentRequest(id.ToString("D"),data.productId,
                data.goodsPrice,value.signData,value.paySignature,value.sessionSignature)).WithServerCallId(response.callId);
        }
        public static bool MatchesOrder(GameOrder order,WeChatPaymentRequest request){
            return order!=null&&request!=null&&order.OrderId==request.OrderId&&order.ProductId==request.ProductId&&
                order.Currency=="CNY"&&order.AmountMinor==request.AmountMinor;
        }
#if UNITY_5_3_OR_NEWER
        public static PreparedRequest<WeChatPaymentRequest> Parse(string json){
            return Evaluate(UnityEngine.JsonUtility.FromJson<WeChatPrepaymentResponse>(json),UnityEngine.JsonUtility.FromJson<WeChatSignData>);
        }
#endif
        static PreparedRequest<WeChatPaymentRequest> Invalid(){return PreparedRequest<WeChatPaymentRequest>.Fail(new SdkError("PAY.INVALID_RESPONSE","Invalid WeChat preparation response"));}
        static bool Text(string value,int max){if(string.IsNullOrWhiteSpace(value)||value.Length>max)return false;foreach(char c in value)if(char.IsControl(c))return false;return true;}
        static bool Digest(string value){if(value==null||value.Length!=64)return false;foreach(char c in value)if(!Uri.IsHexDigit(c))return false;return true;}
        static bool PlatformOrder(string value){if(string.IsNullOrEmpty(value)||value.Length>32||value[0]=='_')return false;foreach(char c in value)if(!((c>='a'&&c<='z')||(c>='A'&&c<='Z')||(c>='0'&&c<='9')||"_-|*@".IndexOf(c)>=0))return false;return true;}
    }
}
#endif
