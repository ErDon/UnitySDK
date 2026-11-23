using System;
using ErDon.UnitySDK;
using ErDon.UnitySDK.WeChat;
class WeChatRuntimeTests {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Main(){
  Action<int> done=null;int invoked=0,count=0;SdkError error=null;
  var runtime=new WeChatRuntime(cb=>{done=cb;invoked++;});
  runtime.Initialize(e=>{error=e;count++;});Check(!runtime.IsReady,"not ready before official completion");
  runtime.Initialize(e=>error=e);Check(error.Code=="INIT.IN_PROGRESS"&&invoked==1,"concurrent init rejected");
  done(1);done(200);Check(count==1&&!runtime.IsReady&&error.Code=="INIT.PLATFORM_FAILED"&&error.PlatformCode=="1","first code and version error");
  runtime.Initialize(e=>error=e);done(0);Check(error.Code=="INIT.PLATFORM_FAILED"&&error.PlatformCode=="0","zero is not WeChat success");
  runtime.Initialize(e=>error=e);done(123);Check(error.Code=="INIT.PLATFORM_FAILED"&&error.PlatformCode=="123","unknown failure retained");
  runtime.Initialize(e=>{error=e;count++;});done(200);Check(runtime.IsReady&&error==null,"success code 200");
  int calls=invoked;runtime.Initialize(e=>error=e);Check(invoked==calls&&error==null,"ready init idempotent");
  runtime.Dispose();runtime.Initialize(e=>error=e);Check(!runtime.IsReady&&error.Code=="SDK.DISPOSED","disposed rejected");
  var late=new WeChatRuntime(cb=>done=cb);late.Initialize(e=>count++);int before=count;late.Dispose();done(200);Check(count==before&&!late.IsReady,"late completion suppressed");
  new WeChatRuntime(cb=>{throw new Exception("private");}).Initialize(e=>error=e);
  Check(error.Code=="INIT.PROVIDER_EXCEPTION"&&!error.Reason.Contains("private"),"safe exception");
  new WeChatRuntime().Dispose();
  Console.WriteLine("WECHAT_RUNTIME_TESTS_PASS (official entry compiled; injected initialization codes)");
 }
}
