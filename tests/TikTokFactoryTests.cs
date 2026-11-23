using System;
using ErDon.UnitySDK;
using ErDon.UnitySDK.TikTok;
class TikTokFactoryTests {
 sealed class Payment:IPay {
  public int Disposes;
  public void Pay(PaymentRequest request,Action<PaymentResult> callback){callback(PaymentResult.Fail("TEST.PENDING","No live payment"));}
  public void Dispose(){Disposes++;}
 }
 sealed class MockExchange:ISessionExchange {
  public int Disposes;
  public void Exchange(PlatformCredential credential,Action<LoginResult> cb){throw new Exception("No live calls expected");}
  public void Dispose(){Disposes++;}
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Main(){
  int created=0;MockExchange exchange=null;
  var factory=new TikTokProviderFactory("game","test",()=>{created++;return exchange=new MockExchange();});
  Check(created==0,"constructor creates no exchange");
  SDK.InitializeWithFactory(new SdkConfig{Platform="TikTok",LoginEnabled=false,StorageEnabled=true,ShareEnabled=true,RewardedAdsEnabled=true},factory,action=>action(),(l,s)=>{});
  Check(created==0,"disabled login never creates exchange");SDK.Shutdown();
  SDK.InitializeWithFactory(new SdkConfig{Platform="TikTok"},factory,action=>action(),(l,s)=>{});
  Check(created==1,"enabled login creates exchange");SDK.Shutdown();Check(exchange.Disposes==1,"SDK owns exchange");
  var a=factory.CreateStorage();var b=factory.CreateStorage();Check(!object.ReferenceEquals(a,b),"fresh storage");a.Dispose();b.Dispose();
  var x=factory.CreateShare();var y=factory.CreateShare();Check(!object.ReferenceEquals(x,y),"fresh share");x.Dispose();y.Dispose();
  bool rejected=false;try{new TikTokProviderFactory("game","test").CreateAuth(z=>z());}catch(InvalidOperationException){rejected=true;}
  Check(rejected,"missing backend rejected");
  rejected=false;try{new TikTokProviderFactory("game","test",()=>null).CreateAuth(z=>z());}catch(InvalidOperationException){rejected=true;}
  Check(rejected,"null backend rejected");
  int payments=0;Payment payment=null;
  var payFactory=new TikTokProviderFactory("game","test",createPayment:post=>{payments++;return payment=new Payment();});
  SDK.InitializeWithFactory(new SdkConfig{Platform="TikTok",LoginEnabled=false,PayEnabled=false},payFactory,z=>z(),(l,s)=>{});
  Check(payments==0,"disabled pay never creates backend or gateway");SDK.Shutdown();
  SDK.InitializeWithFactory(new SdkConfig{Platform="TikTok",LoginEnabled=false,PayEnabled=true},payFactory,z=>z(),(l,s)=>{});
  Check(payments==1,"enabled pay creates provider once");
  PaymentResult result=null;SDK.Pay(new PaymentRequest("session","product","request"),r=>result=r);
  Check(result.Error.Code=="TEST.PENDING","aggregated SDK routes payment to injected provider");
  SDK.Shutdown();Check(payment.Disposes==1,"SDK owns payment provider");
  rejected=false;try{new TikTokProviderFactory("game","test").CreatePay(z=>z());}catch(InvalidOperationException){rejected=true;}
  Check(rejected,"payment is not implicitly enabled without backend");
  rejected=false;try{new TikTokProviderFactory("game","test",createPayment:post=>null).CreatePay(z=>z());}catch(InvalidOperationException){rejected=true;}
  Check(rejected,"null payment provider rejected");
  Console.WriteLine("TIKTOK_FACTORY_TESTS_PASS (configuration and ownership; no platform calls)");
 }
}
