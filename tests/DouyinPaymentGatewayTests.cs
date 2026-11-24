using System;
using ErDon.UnitySDK;
using ErDon.UnitySDK.Douyin;
using TTSDK;
class DouyinPaymentGatewayTests {
 sealed class Preparation:IPreparedRequestClient<DouyinPaymentRequest> {
  public Action<PreparedRequest<DouyinPaymentRequest>> Callback;public int Disposals;
  public void Prepare(string id,Action<PreparedRequest<DouyinPaymentRequest>> callback){Callback=callback;}
  public void Dispose(){Disposals++;}
 }
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
  var id=Guid.NewGuid().ToString("D");var order=new GameOrder(id,"sword",600,"CNY",OrderState.Created);
  var data=new DouyinPrepaymentResponse{callId=Guid.NewGuid().ToString("D"),prepayment=new DouyinPrepaymentData{
   orderId=id,route="Android",zoneId="1",extraInfo="server-nonce",productName="sword",amountMinor=600,buyQuantity=0,directPurchase=true}};
  var prepared=DouyinPaymentPreparation.Evaluate(data,DouyinPaymentRoute.Android);
  Check(prepared.Error==null&&prepared.ServerCallId==data.callId&&DouyinPaymentPreparation.MatchesOrder(order,prepared.Value),"Validated typed preparation");
  Check(DouyinPaymentPreparation.Evaluate(data,DouyinPaymentRoute.IosCustomerService).Error.Code=="PAY.INVALID_RESPONSE","Wrong route rejected");
  Preparation client=null;string session=null;int preparedCount=0;
  gateway=new DouyinPaymentGateway(DouyinPaymentRoute.Android,p=>{android=p;calls++;},p=>ios=p,()=>true);
  var launcher=new PreparedPaymentLauncher<DouyinPaymentRequest>(s=>{session=s;preparedCount++;return client=new Preparation();},gateway,DouyinPaymentPreparation.MatchesOrder);
  launcher.Launch(new PaymentRequest("account-a","sword","purchase"),order,r=>result=r);
  var first=client;var dispatchBefore=calls;
  launcher.Launch(new PaymentRequest("account-b","sword","purchase"),order,r=>result=r);
  Check(result.Error.Code=="PAY.IN_PROGRESS"&&preparedCount==1,"Concurrent preparation rejected");
  first.Callback(prepared);first.Callback(prepared);
  Check(calls==dispatchBefore+1&&session=="account-a"&&android.OrderAmount==600,"One typed gateway launch with current session");
  android.Success(new RequestGamePaymentSuccessResult());
  Check(result.RequiresServerQuery&&first.Disposals==1,"Prepared client released after platform completion");
  launcher.Launch(new PaymentRequest("account-b","sword","purchase"),order,r=>result=r);
  Check(session=="account-b"&&preparedCount==2,"Changed account receives fresh preparation client");
  data.prepayment.amountMinor=1;dispatchBefore=calls;
  client.Callback(DouyinPaymentPreparation.Evaluate(data,DouyinPaymentRoute.Android));
  Check(result.Error.Code=="PAY.INVALID_RESPONSE"&&calls==dispatchBefore&&client.Disposals==1,"Server/order amount mismatch never launches gateway");
  launcher.Launch(new PaymentRequest("account-b","sword","purchase"),order,r=>result=r);result=null;
  launcher.Dispose();client.Callback(prepared);
  Check(result==null&&client.Disposals==1&&calls==dispatchBefore,"Disposal suppresses late preparation response");
  Console.WriteLine("DOUYIN_PAYMENT_GATEWAY_TESTS_PASS (official binary, injected callbacks; no real payment)");
 }
}
