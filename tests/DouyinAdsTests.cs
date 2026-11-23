using System;using ErDon.UnitySDK;using ErDon.UnitySDK.Douyin;
class DouyinAdsTests {
 sealed class Ad:IDouyinRewardedAd {
  public event Action Loaded;public event Action<int> Failed;public event Action<bool> Closed;
  public int Loads,Shows,Destroys;public bool ThrowShow;
  public void Load(){Loads++;}public void Show(){Shows++;if(ThrowShow)throw new Exception("secret");}public void Destroy(){Destroys++;}
  public void Ready(){if(Loaded!=null)Loaded();}public void Error(int c){if(Failed!=null)Failed(c);}public void Close(bool ended){if(Closed!=null)Closed(ended);}
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Main(){
  var ad=new Ad();int count=0;RewardedAdResult result=null;string id=null;
  var provider=new DouyinRewardedAds(s=>{id=s;return ad;});
  Action<RewardedAdResult> capture=r=>{result=r;count++;};
  provider.Show(new RewardedAdRequest("unit"),capture);Check(id=="unit"&&ad.Loads==1&&ad.Shows==0,"load before show");
  provider.Show(new RewardedAdRequest("unit"),r=>result=r);Check(result.Error.Code=="ADS.IN_PROGRESS","concurrent rejected");
  ad.Ready();ad.Ready();Check(ad.Shows==1,"only show once");ad.Close(false);ad.Close(true);Check(count==1&&result.Success&&!result.Completed&&ad.Destroys==1,"early close no reward and release");
  ad=new Ad();provider.Show(new RewardedAdRequest("unit"),capture);ad.Ready();ad.Close(true);Check(result.Completed&&result.Success,"completed distinct");
  ad=new Ad();provider.Show(new RewardedAdRequest("unit"),capture);ad.Error(1004);Check(!result.Success&&!result.Completed&&result.Error.PlatformCode=="1004"&&result.Error.PlatformMessage==null,"error code only");
  ad=new Ad();provider.Show(new RewardedAdRequest("unit"),capture);ad.Close(true);Check(result.Error.Code=="ADS.INVALID_STATE"&&!result.Completed,"close before show not reward");
  ad=new Ad{ThrowShow=true};provider.Show(new RewardedAdRequest("unit"),capture);ad.Ready();Check(result.Error.Code=="ADS.SHOW_FAILED"&&ad.Destroys==1,"show failure cleanup");
  ad=new Ad();provider.Show(new RewardedAdRequest("unit"),capture);int before=count;provider.Dispose();ad.Ready();ad.Close(true);Check(count==before&&ad.Destroys==1,"dispose suppresses late callback");
  provider.Show(new RewardedAdRequest("unit"),capture);Check(result.Error.Code=="SDK.DISPOSED","disposed rejected");
  new DouyinRewardedAds(s=>null).Show(new RewardedAdRequest("unit"),capture);Check(result.Error.Code=="ADS.CREATE_FAILED","null create rejected");
  new DouyinRewardedAds().Dispose();Console.WriteLine("DOUYIN_ADS_TESTS_PASS (official APIs compiled; injected ad lifecycle)");
 }
}
