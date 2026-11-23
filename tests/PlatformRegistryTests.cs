using System;
using ErDon.UnitySDK;
class PlatformRegistryTests {
 sealed class Runtime:IPlatformRuntime {
  public int Disposes,Starts;public string Platform{get;set;}public bool IsReady{get;private set;}
  public void Initialize(Action<SdkError> cb){Starts++;IsReady=true;cb(null);}public void Dispose(){Disposes++;}
 }
 sealed class Factory:IPlatformSdkProviderFactory {
  public string Platform{get{return "WeChat";}}
  public IAuth CreateAuth(Action<Action> post){throw new Exception("login should remain disabled");}
  public IStorage CreateStorage(){throw new Exception("storage should remain disabled");}
 }
 sealed class Network:INetwork {
  public int Disposes;public INetworkOperation Send(NetworkRequest r,Action<NetworkResult> cb){throw new Exception("No network call expected");}public void Dispose(){Disposes++;}
 }
 static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
 static void Reject(Action action,string code){try{action();}catch(InvalidOperationException e){Check(e.Message==code,"wrong rejection: "+e.Message);return;}throw new Exception("Expected "+code);}
 static void Main(){
  var data=new SdkRuntimeConfiguration{platform="WeChat",gameId="game",appId="app",login=false};
  Func<SdkStartup> create=()=>SdkPlatformRegistry.CreateStartup(data,null,a=>a(),(l,s)=>{},r=>{});
  Reject(()=>create(),"CONFIG.PLATFORM_NOT_REGISTERED");
  int made=0;Runtime runtime=null;string game=null,environment=null;Func<ISessionExchange> exchange=null;
  SdkPlatformRegistry.Register("WeChat",()=>{made++;return runtime=new Runtime{Platform="WeChat"};},(g,e,x)=>{game=g;environment=e;exchange=x;return new Factory();});
  Reject(()=>SdkPlatformRegistry.Register("WeChat",()=>null,(g,e,x)=>null),"CONFIG.DUPLICATE_PLATFORM");
  data.payments=true;Reject(()=>create(),"CONFIG.PAYMENT_UNIMPLEMENTED");Check(made==0,"unsupported payment constructed runtime");
  data.payments=false;data.login=true;Reject(()=>create(),"CONFIG.SESSION_EXCHANGE_REQUIRED");Check(made==0,"missing exchange constructed runtime");
  data.login=false;data.production=true;bool done=false;
  var attempt=SdkPlatformRegistry.CreateStartup(data,()=>{throw new Exception("disabled exchange called");},a=>a(),(l,s)=>{},r=>done=r.Success);
  Check(game=="game"&&environment=="production"&&exchange==null&&runtime.Starts==0,"configuration and deferred initialization");
  data.login=true;data.platform="TikTok";attempt.Start();attempt.Tick();Check(done&&runtime.Starts==1&&runtime.Disposes==1,"snapshot and composition");attempt.Dispose();Check(runtime.Disposes==1,"single runtime ownership");SDK.Shutdown();
  SdkPlatformRegistry.Reset();data.platform="WeChat";data.login=false;
  SdkPlatformRegistry.Register("WeChat",()=>runtime=new Runtime{Platform="Douyin"},(g,e,x)=>new Factory());
  Reject(()=>create(),"CONFIG.PLATFORM_MISMATCH");Check(runtime.Disposes==1,"mismatched runtime released");
  SdkPlatformRegistry.Reset();Reject(()=>create(),"CONFIG.PLATFORM_NOT_REGISTERED");
  int networkCreated=0;var transport=new Network();
  SdkPlatformRegistry.Register("WeChat",()=>new Runtime{Platform="WeChat"},(g,e,x)=>new Factory());data.network=true;
  Reject(()=>create(),"CONFIG.NETWORK_PROVIDER_REQUIRED");
  var networkStartup=SdkPlatformRegistry.CreateStartup(data,null,a=>a(),(l,s)=>{},r=>done=r.Success,createNetwork:()=>{networkCreated++;return transport;});
  Check(networkCreated==0,"transport deferred until runtime ready");networkStartup.Start();networkStartup.Tick();
  Check(done&&networkCreated==1,"configured network created once");SDK.Shutdown();Check(transport.Disposes==1,"SDK owns injected transport");
  data.network=false;var disabledStartup=SdkPlatformRegistry.CreateStartup(data,null,a=>a(),(l,s)=>{},r=>done=r.Success,createNetwork:()=>{throw new Exception("disabled transport constructed");});
  disabledStartup.Start();disabledStartup.Tick();Check(done,"disabled network does not invoke constructor");SDK.Shutdown();SdkPlatformRegistry.Reset();
  Console.WriteLine("PASS: configured platform selection, preflight, snapshot, ownership and session reset");
 }
}
