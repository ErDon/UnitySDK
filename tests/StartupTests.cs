using System;
using System.Collections.Generic;
using System.Threading;
using ErDon.UnitySDK;
class StartupTests {
 sealed class Runtime:IPlatformRuntime { public string Platform {get{return "Douyin";}} public bool IsReady {get;set;} public Action<SdkError> Done;public int Disposes;public void Initialize(Action<SdkError> cb){Done=cb;}public void Dispose(){Disposes++;}}
 sealed class Factory:IPlatformSdkProviderFactory {public string Platform {get{return "Douyin";}}public int Created;public IAuth CreateAuth(Action<Action> d){Created++;return new Auth();}public IStorage CreateStorage(){throw new Exception();}}
 sealed class Auth:IAuth {public void Login(Action<LoginResult> cb){cb(LoginResult.Ok("session"));}public void Dispose(){}}
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);}
 static void Main(){
  var logs=new List<string>();var r=new Runtime();var f=new Factory();StartupResult result=null;int count=0;
  var startup=new SdkStartup(new SdkConfig{Platform="Douyin"},r,f,a=>a(),(l,s)=>logs.Add(s),x=>{result=x;count++;});
  startup.Start();Check(f.Created==0,"no capabilities before ready");r.IsReady=true;
  var worker=new Thread(()=>r.Done(null));worker.Start();worker.Join();Check(count==0,"worker callback deferred to tick");startup.Tick();
  Check(result.Success&&f.Created==1&&r.Disposes==1,"ready composes and releases bridge");r.Done(new SdkError("late","private"));startup.Tick();Check(count==1,"one result");
  LoginResult login=null;SDK.Login(x=>login=x);Check(login.Success,"ready SDK usable");
  var failing=new Runtime();var failFactory=new Factory();result=null;
  var failed=new SdkStartup(new SdkConfig{Platform="Douyin"},failing,failFactory,a=>a(),(l,s)=>logs.Add(s),x=>result=x);
  failed.Start();failing.Done(new SdkError("INIT.SDK_VERSION_UNSUPPORTED","safe","1",null));failed.Tick();
  Check(result.Error.PlatformCode=="1"&&result.CallId==result.Error.CallId&&failFactory.Created==0,"error correlation without creation");
  SDK.Login(x=>login=x);Check(login.Success,"failure preserves active SDK");
  var cancelRuntime=new Runtime{IsReady=true};var cancelled=new SdkStartup(new SdkConfig{Platform="Douyin"},cancelRuntime,new Factory(),a=>a(),(l,s)=>logs.Add(s),x=>result=x);
  cancelled.Start();cancelRuntime.Done(null);cancelled.Cancel();cancelled.Tick();Check(result.Error.Code=="INIT.CANCELLED","cancel queued success");
  var hanging=new Runtime();var timed=new SdkStartup(new SdkConfig{Platform="Douyin"},hanging,new Factory(),a=>a(),(l,s)=>logs.Add(s),x=>result=x,1);
  timed.Start();Thread.Sleep(10);timed.Tick();Check(result.Error.Code=="INIT.TIMEOUT"&&hanging.Disposes==1,"timeout releases");
  Check(logs.Exists(s=>s.Contains("interface=SDK.Startup")&&s.Contains("timeoutMilliseconds")&&s.Contains("logId=")),"formatted startup logs");
  startup.Dispose();failed.Dispose();cancelled.Dispose();timed.Dispose();SDK.Shutdown();
  Console.WriteLine("PASS: startup readiness, deferred completion, error correlation, preservation, cancellation and timeout");
 }
}
