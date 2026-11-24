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
  var response=new WeChatPrepaymentResponse{prepayment=new WeChatPrepaymentData{orderId=Guid.NewGuid().ToString("D"),signData="{\"preserve\": true}",paySignature=new string('a',64),sessionSignature=new string('b',64)}};
  var sign=new WeChatSignData{mode="goods",offerId="offer",currencyType="CNY",platform="android",zoneId="1",productId="sword",outTradeNo=new string('a',32),attach="server-nonce",buyQuantity=1,env=1,goodsPrice=600};
  var prepared=WeChatPaymentPreparation.Evaluate(response,_=>sign);
  Check(prepared.Error==null&&prepared.Value.SignData==response.prepayment.signData&&prepared.Value.ProductId=="sword"&&prepared.Value.AmountMinor==600,"Typed preparation preserves signed bytes");
  sign.outTradeNo=new string('a',33);Check(WeChatPaymentPreparation.Evaluate(response,_=>sign).Error!=null,"Platform order limit");
  sign.outTradeNo="ok";sign.buyQuantity=2;Check(WeChatPaymentPreparation.Evaluate(response,_=>sign).Error!=null,"Quantity must match unit catalog");
  Check(WeChatPaymentPreparation.Evaluate(response,_=>{throw new Exception("private");}).Error.Code=="PAY.INVALID_RESPONSE","Parser exceptions sanitized");
  Console.WriteLine("WECHAT_PAYMENT_GATEWAY_TESTS_PASS (official signature compile and injected callbacks; no real payment)");
 }
}
