using System;
using ErDon.UnitySDK;
using ErDon.UnitySDK.WeChat;
class WeChatFactoryTests {
 sealed class MockExchange:ISessionExchange {
  public int Disposes;
  public void Exchange(PlatformCredential credential,Action<LoginResult> cb){throw new Exception("No live calls expected");}
  public void Dispose(){Disposes++;}
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Main(){
  int created=0;MockExchange exchange=null;
  var factory=new WeChatProviderFactory("game","test",()=>{created++;return exchange=new MockExchange();});
  Check(created==0,"constructor creates no exchange");
  SDK.InitializeWithFactory(new SdkConfig{Platform="WeChat",LoginEnabled=false,StorageEnabled=true,ShareEnabled=true},factory,action=>action(),(l,s)=>{});
  Check(created==0,"disabled login never creates exchange");SDK.Shutdown();
  SDK.InitializeWithFactory(new SdkConfig{Platform="WeChat"},factory,action=>action(),(l,s)=>{});
  Check(created==1,"enabled login creates exchange");SDK.Shutdown();Check(exchange.Disposes==1,"SDK owns exchange");
  var a=factory.CreateStorage();var b=factory.CreateStorage();Check(!object.ReferenceEquals(a,b),"fresh storage");a.Dispose();b.Dispose();
  var x=factory.CreateShare();var y=factory.CreateShare();Check(!object.ReferenceEquals(x,y),"fresh share");x.Dispose();y.Dispose();
  bool rejected=false;try{new WeChatProviderFactory("game","test").CreateAuth(z=>z());}catch(InvalidOperationException){rejected=true;}
  Check(rejected,"missing backend rejected");
  rejected=false;try{new WeChatProviderFactory("game","test",()=>null).CreateAuth(z=>z());}catch(InvalidOperationException){rejected=true;}
  Check(rejected,"null backend rejected");
  Console.WriteLine("WECHAT_FACTORY_TESTS_PASS (configuration and ownership; no platform calls)");
 }
}

