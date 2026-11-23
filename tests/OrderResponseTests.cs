using System;
using ErDon.UnitySDK;
class OrderResponseTests
{
    static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
    static void Main(){
        var id=Guid.NewGuid().ToString("D");var correlation=Guid.NewGuid().ToString("D");
        var data=new OrderResponseData{callId=correlation,order=new OrderData{orderId=id,productId="coins",amountMinor=99,currency="USD",state="Created"}};
        var result=OrderResponsePolicy.Evaluate(200,data,id,"coins");
        Check(result.Success&&result.Order.State==OrderState.Created&&result.ServerCallId==correlation,"Create is not paid");
        data.order.state="Paid";Check(OrderResponsePolicy.Evaluate(200,data).Order.State==OrderState.Paid,"Paid is distinct");
        data.order.state="Fulfilled";Check(OrderResponsePolicy.Evaluate(200,data).Order.State==OrderState.Fulfilled,"Fulfilled explicit");
        Check(!OrderResponsePolicy.Evaluate(200,data,Guid.NewGuid().ToString("D")).Success,"Wrong order rejected");
        Check(!OrderResponsePolicy.Evaluate(200,data,null,"other").Success,"Wrong product rejected");
        foreach(var state in new[]{"0","Unknown","paid",null}){data.order.state=state;Check(!OrderResponsePolicy.Evaluate(200,data).Success,"Unknown state rejected");}
        data.order.state="Paid";data.order.amountMinor=9007199254740992L;Check(!OrderResponsePolicy.Evaluate(200,data).Success,"Unsafe amount rejected");
        data.order.amountMinor=99;data.errorCode="private-error";Check(!OrderResponsePolicy.Evaluate(200,data).Success,"Conflicting success rejected");
        var error=OrderResponsePolicy.Evaluate(429,null,retryAfter:"30");Check(error.Error.RetryAfterSeconds==30,"Cooldown retained");
        Check(OrderResponsePolicy.Evaluate(429,null,retryAfter:"-30").Error.RetryAfterSeconds==null,"Invalid cooldown ignored");
        data.callId="secret\nlog";Check(OrderResponsePolicy.Evaluate(500,data).ServerCallId==null,"Invalid correlation rejected");
        Check(OrderResponsePolicy.Evaluate(500,data).Error.Code=="PAY.SERVER_REJECTED","Unknown server text hidden");
        data.errorCode="AUTH.SESSION_INVALID";Check(OrderResponsePolicy.Evaluate(401,data).Error.Retry==RetryPolicy.UserAction,"Session refresh needed");
        Console.WriteLine("PASS: order state, identity correlation, amount validation and safe errors");
    }
}
