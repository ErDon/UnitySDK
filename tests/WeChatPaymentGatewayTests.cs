using System;
using ErDon.UnitySDK;
using ErDon.UnitySDK.WeChat;
using WeChatWASM;
class WeChatPaymentGatewayTests {
 static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
 static void Main(){
  RequestMidasPaymentGameItemOption sent=null;int calls=0,callbacks=0;
  var gateway=new WeChatPaymentGateway(p=>{sent=p;calls++;});
  var request=new WeChatPaymentRequest(" {\n\"outTradeNo\":\"server-order\" } ",new string('a',64),new string('B',64));
  PaymentLaunchResult result=null;
  gateway.Launch(request,r=>{result=r;callbacks++;});
  Check(sent.signData==request.SignData&&sent.paySig==request.PaySignature&&sent.signature==request.SessionSignature,"Signed payload is preserved byte-for-byte");
  gateway.Launch(request,r=>result=r);Check(result.Error.Code=="PAY.IN_PROGRESS"&&calls==1,"Busy guard");
  sent.success(new MidasPaymentError{errCode=0,errMsg="private"});sent.fail(new MidasPaymentError{errCode=1});
  Check(result.RequiresServerQuery&&callbacks==1,"First terminal only; no client evidence fabricated");
  gateway.Launch(request,r=>result=r);sent.success(new MidasPaymentError{errCode=7,errMsg="private"});
  Check(result.Error.Code=="PAY.PLATFORM_REJECTED"&&result.Error.PlatformCode=="7"&&!result.Error.Reason.Contains("private"),"Nonzero success envelope rejected safely");
  gateway.Launch(new WeChatPaymentRequest("{}","bad",new string('a',64)),r=>result=r);
  Check(result.Error.Code=="PAY.INVALID_REQUEST"&&calls==2,"Invalid signature rejected before dispatch");
  gateway.Launch(request,r=>callbacks++);gateway.Dispose();sent.success(new MidasPaymentError{errCode=0});
  Check(callbacks==1,"Dispose suppresses late response");
  gateway.Launch(request,r=>result=r);Check(result.Error.Code=="SDK.DISPOSED","Disposed call rejected");
  gateway=new WeChatPaymentGateway(p=>{throw new Exception("private-signature");});
  gateway.Launch(request,r=>result=r);Check(result.Error.Code=="PAY.LAUNCH_FAILED"&&!result.Error.Reason.Contains("private"),"Invocation error sanitized");
  Check(!request.ToString().Contains("server-order"),"Request formatting redacted");
  Console.WriteLine("WECHAT_PAYMENT_GATEWAY_TESTS_PASS (official signature compile and injected callbacks; no real payment)");
 }
}
