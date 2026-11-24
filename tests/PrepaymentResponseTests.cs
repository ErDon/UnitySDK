using System;
using ErDon.UnitySDK;
class PrepaymentResponseTests
{
    static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
    static void Main(){
        string id=Guid.NewGuid().ToString("D"),call=Guid.NewGuid().ToString("D");
        var data=new PrepaymentResponseData{prepayment=new PrepaymentData{orderId=id,tradeOrderId="trade"}};
        Check(PrepaymentResponsePolicy.Evaluate(200,data,id).TradeOrderId=="trade","Valid mapping");
        Check(PrepaymentResponsePolicy.Evaluate(200,data,Guid.NewGuid().ToString("D")).Error.Code=="PAY.INVALID_RESPONSE","Wrong order");
        foreach(string value in new[]{"", " bad", "bad\n",new string('a',257)}){data.prepayment.tradeOrderId=value;Check(PrepaymentResponsePolicy.Evaluate(200,data,id).Error!=null,"Bad platform id");}
        data=new PrepaymentResponseData{errorCode="PAY.PREPAYMENT_UNCERTAIN",callId=call};
        var error=PrepaymentResponsePolicy.Evaluate(409,data,id).Error;
        Check(error.Code==data.errorCode&&error.ServerCallId==call&&error.Retry==RetryPolicy.UserAction,"Uncertain is not automatic retry");
        Check(PrepaymentResponsePolicy.Evaluate(200,data,id).Error.Code=="PAY.INVALID_RESPONSE","Contradictory status rejected");
        data.errorCode="PAY.PRODUCT_CHANGED";error=PrepaymentResponsePolicy.Evaluate(409,data,id).Error;
        Check(error.Code=="PAY.PRODUCT_CHANGED"&&error.Retry==RetryPolicy.UserAction&&error.ServerCallId==call,"Changed catalog retains safe actionable code");
        foreach(var code in new[]{"PAY.CREDENTIAL_REFRESH_PENDING","PAY.CREDENTIAL_REFRESH_UNCERTAIN","PAY.SESSION_EXPIRED","PAY.CONFIG_CHANGED"}){
            data.errorCode=code;var status=code=="PAY.SESSION_EXPIRED"?401:code=="PAY.CONFIG_CHANGED"?409:503;
            error=PrepaymentResponsePolicy.Evaluate(status,data,id).Error;
            Check(error.Code==code&&error.ServerCallId==call&&error.Retry==(code=="PAY.CREDENTIAL_REFRESH_PENDING"?RetryPolicy.Delayed:RetryPolicy.UserAction),"Payment recovery reason and retry policy");
        }
        data.errorCode="private-upstream-error";data.callId="private";error=PrepaymentResponsePolicy.Evaluate(500,data,id).Error;
        Check(error.Code=="PAY.SERVER_REJECTED"&&error.ServerCallId==null&&!error.Reason.Contains("private"),"Unknown text not forwarded");
        error=PrepaymentResponsePolicy.Evaluate(429,null,id,"7").Error;Check(error.RetryAfterSeconds==7,"Rate limit delay");
        Console.WriteLine("PREPAYMENT_RESPONSE_TESTS_PASS");
    }
}
