using System;
using ErDon.UnitySDK;
using ErDon.UnitySDK.Douyin;
using TTSDK;
class DouyinPaymentGatewayTests {
 static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
 static ErrorInfo Cancelled(){var error=(ErrorInfo)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(ErrorInfo));error.ErrorCode=-2;error.ErrMsg="private";return error;}
 static void Main(){
  RequestGamePaymentParam android=null;OpenAwemeCustomerServiceParam ios=null;int calls=0,count=0;PaymentLaunchResult result=null;
  var gateway=new DouyinPaymentGateway(DouyinPaymentRoute.Android,p=>{android=p;calls++;},p=>ios=p,()=>true);
  var request=new DouyinPaymentRequest("order","1","private-extra",0,99,"coins",true);
  gateway.Launch(request,r=>{result=r;count++;});
  Check(android.CustomId=="order"&&android.Mode=="game"&&android.Env==0&&android.CurrencyType=="CNY"&&android.Platform=="android","Android fixed fields");
  Check(android.GoodType==2&&android.BuyQuantity==0&&android.OrderAmount==99&&android.ExtraInfo=="private-extra","Direct purchase server fields");
  gateway.Launch(request,r=>result=r);Check(result.Error.Code=="PAY.IN_PROGRESS"&&calls==1,"Busy guard");
  android.Success(new RequestGamePaymentSuccessResult());android.Fail(Cancelled());
  Check(result.RequiresServerQuery&&count==1,"Success is only server-query signal, duplicate ignored");
  gateway.Launch(request,r=>result=r);android.Fail(Cancelled());
  Check(result.Error.Code=="PAY.USER_CANCELLED"&&result.Error.PlatformCode=="-2"&&!result.Error.Reason.Contains("private"),"Cancellation and safe error");
  gateway.Dispose();gateway.Launch(request,r=>result=r);Check(result.Error.Code=="SDK.DISPOSED","Disposed rejected");
  gateway=new DouyinPaymentGateway(DouyinPaymentRoute.IosCustomerService,p=>android=p,p=>ios=p,()=>true);
  gateway.Launch(new DouyinPaymentRequest("ios-order","zone",null,100),r=>result=r);
  Check(ios.CurrencyType=="DIAMOND"&&ios.BuyQuantity==100&&ios.CustomId=="ios-order","iOS coin route");
  gateway.Dispose();result=null;ios.Success(new OpenAwemeCustomerSuccessResult());Check(result==null,"Late iOS callback suppressed");
  gateway=new DouyinPaymentGateway(DouyinPaymentRoute.Android,p=>calls++,p=>calls++,()=>false);var before=calls;
  gateway.Launch(request,r=>result=r);Check(result.Error.Code=="CAPABILITY.NOT_SUPPORTED"&&calls==before,"Unsupported direct purchase does not dispatch");
  gateway.Launch(new DouyinPaymentRequest("order","1",null,10,99,"coins",true),r=>result=r);
  Check(result.Error.Code=="PAY.INVALID_REQUEST"&&calls==before,"Mutually exclusive payment fields enforced");
  Check(!request.ToString().Contains("private"),"No request contents formatted");
  Console.WriteLine("DOUYIN_PAYMENT_GATEWAY_TESTS_PASS (official binary, injected callbacks; no real payment)");
 }
}
