using System;
using ErDon.UnitySDK;
using ErDon.UnitySDK.TikTok;
class TikTokRuntimeTests {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Main(){
  Action<int> done=null;int invoked=0,count=0;SdkError error=null;
  var runtime=new TikTokRuntime(cb=>{done=cb;invoked++;});
  runtime.Initialize(e=>{error=e;count++;});Check(!runtime.IsReady,"not ready before official completion");
  runtime.Initialize(e=>error=e);Check(error.Code=="INIT.IN_PROGRESS"&&invoked==1,"concurrent init rejected");
  done(1);done(0);Check(count==1&&!runtime.IsReady&&error.Code=="INIT.PLATFORM_FAILED"&&error.PlatformCode=="1","first failure retained");
  runtime.Initialize(e=>error=e);done(200);Check(error.Code=="INIT.PLATFORM_FAILED"&&error.PlatformCode=="200","200 is not TikTok success");
  runtime.Initialize(e=>error=e);done(123);Check(error.Code=="INIT.PLATFORM_FAILED"&&error.PlatformCode=="123","unknown failure retained");
  runtime.Initialize(e=>{error=e;count++;});done(0);Check(runtime.IsReady&&error==null,"success code zero");
  int calls=invoked;runtime.Initialize(e=>error=e);Check(invoked==calls&&error==null,"ready init idempotent");
  runtime.Dispose();runtime.Initialize(e=>error=e);Check(!runtime.IsReady&&error.Code=="SDK.DISPOSED","disposed rejected");
  var late=new TikTokRuntime(cb=>done=cb);late.Initialize(e=>count++);int before=count;late.Dispose();done(0);Check(count==before&&!late.IsReady,"late completion suppressed");
  new TikTokRuntime(cb=>{throw new Exception("private");}).Initialize(e=>error=e);
  Check(error.Code=="INIT.PROVIDER_EXCEPTION"&&!error.Reason.Contains("private"),"safe exception");
  new TikTokRuntime().Dispose();
  Console.WriteLine("TIKTOK_RUNTIME_TESTS_PASS (local plugin entry compiled; injected initialization codes)");
 }
}
