using System;using System.Collections.Generic;using System.Threading;using ErDon.UnitySDK;
class RewardedFacadeTests {
 sealed class Ads:IRewardedAds {public Action<RewardedAdResult> Done;public int Disposes;public void Show(RewardedAdRequest r,Action<RewardedAdResult> cb){Done=cb;}public void Dispose(){Disposes++;}}
 sealed class PayProvider:IPay {public void Pay(PaymentRequest r,Action<PaymentResult> cb){cb(PaymentResult.Fail("PAY.TEST","test"));}public void Dispose(){}}
 sealed class Factory:ISdkProviderFactory,IRewardedAdsProviderFactory {public int Created;public Ads Ads;public IAuth CreateAuth(Action<Action> d){throw new Exception();}public IStorage CreateStorage(){throw new Exception();}public IRewardedAds CreateRewardedAds(){Created++;return Ads=new Ads();}}
 static void Check(bool ok,string s){if(!ok)throw new Exception(s);}
 static void Main(){
  var logs=new List<string>();var f=new Factory();RewardedAdResult result=null;int count=0;
  var request=new RewardedAdRequest("PRIVATE_UNIT");
  SDK.InitializeWithFactory(new SdkConfig{LoginEnabled=false},f,a=>a(),(l,s)=>logs.Add(s));
  SDK.ShowRewardedAd(request,r=>result=r);Check(result.Error.Code=="MODULE.DISABLED"&&f.Created==0,"disabled creates nothing");
  SDK.InitializeWithFactory(new SdkConfig{LoginEnabled=false,RewardedAdsEnabled=true},f,a=>a(),(l,s)=>logs.Add(s));
  SDK.ShowRewardedAd(request,r=>{result=r;count++;});SDK.ShowRewardedAd(request,r=>result=r);Check(result.Error.Code=="ADS.IN_PROGRESS","busy guard");
  f.Ads.Done(RewardedAdResult.Closed(false));f.Ads.Done(RewardedAdResult.Closed(true));Check(count==1&&result.Success&&!result.Completed,"first close only");
  string first=result.CallId;SDK.ShowRewardedAd(request,r=>result=r);var late=f.Ads.Done;SDK.CancelRewardedAd();late(RewardedAdResult.Closed(true));
  Check(result.Error.Code=="ADS.CANCELLED"&&result.Error.CallId==result.CallId&&result.CallId!=first,"cancel correlation and late suppression");
  SDK.ShowRewardedAd(request,r=>result=r,1);Thread.Sleep(10);SDK.Tick();Check(result.Error.Code=="ADS.TIMEOUT","timeout");
  var old=f.Ads;SDK.ShowRewardedAd(request,r=>count++);var stale=old.Done;
  var next=new Ads();SDK.Initialize(new SdkConfig{LoginEnabled=false,RewardedAdsEnabled=true,PayEnabled=true},null,a=>a(),(l,s)=>logs.Add(s),null,null,new PayProvider(),next);
  stale(RewardedAdResult.Closed(true));Check(count==1&&old.Disposes==1,"replacement suppresses old completion");
  SDK.ShowRewardedAd(request,r=>result=r);SDK.Pay(new PaymentRequest("s","p","i"),r=>{});SDK.ShowRewardedAd(request,r=>result=r);
  Check(result.Error.Code=="ADS.IN_PROGRESS","pay completion does not reset ad state");next.Done(RewardedAdResult.Closed(true));Check(result.Completed,"complete reported");
  SDK.Shutdown();Check(next.Disposes==1,"owned provider disposed");
  Check(!string.Join("",logs).Contains("PRIVATE_UNIT")&&logs.Exists(s=>s.Contains("SDK.ShowRewardedAd")&&s.Contains("callId=")),"logs redacted and correlated");
  Console.WriteLine("PASS: rewarded facade configuration, ownership, independent concurrency, cancellation, timeout and logs");
 }
}
