using System;
using System.Collections.Generic;
using System.Threading;
using ErDon.UnitySDK;
class DispatchTests {
 sealed class Provider:IAuth,IShare,IPay {
  public Action<LoginResult> LoginDone; public Action<ShareResult> ShareDone; public Action<PaymentResult> PayDone;
  public void Login(Action<LoginResult> cb){LoginDone=cb;}
  public void Share(ShareRequest r,Action<ShareResult> cb){ShareDone=cb;}
  public void Pay(PaymentRequest r,Action<PaymentResult> cb){PayDone=cb;}
  public void Dispose(){}
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Main(){
  var logs=new List<string>();int main=Thread.CurrentThread.ManagedThreadId;
  foreach(bool queued in new[]{false,true}) {
   var p=new Provider(); var queue=new Queue<Action>();int count=0;
   SDK.Initialize(new SdkConfig{ShareEnabled=true,PayEnabled=true},p,a=>{if(queued)queue.Enqueue(a);throw new Exception("PRIVATE");},(l,s)=>logs.Add(s),null,p,p);
   SDK.Login(r=>{Check(r.Success,"login preserved");Check(Thread.CurrentThread.ManagedThreadId==main,"main thread");count++;});
   SDK.Share(new ShareRequest("title"),r=>{Check(r.Success,"share preserved");count++;});
   SDK.Pay(new PaymentRequest("session","product","request"),r=>{Check(r.Error.Code=="PAY.TEST","pay failure preserved");count++;});
   var worker=new Thread(()=>{p.LoginDone(LoginResult.Ok("session"));p.ShareDone(ShareResult.Dispatched());p.PayDone(PaymentResult.Fail("PAY.TEST","test"));});worker.Start();worker.Join();
   Check(count==0,"no callback on failed worker dispatch");SDK.Tick();Check(count==3,"all recovered");
   while(queue.Count>0)queue.Dequeue()();SDK.Tick();Check(count==3,"queued before throw cannot duplicate");
   SDK.Login(r=>count++);p.LoginDone(LoginResult.Ok("second"));SDK.Tick();Check(count==4,"busy cleared");
   SDK.Share(new ShareRequest("title"),r=>count++);p.ShareDone(ShareResult.Dispatched());
   SDK.Shutdown();SDK.Tick();while(queue.Count>0)queue.Dequeue()();Check(count==4,"shutdown invalidates recovery");
  }
  Check(logs.Exists(s=>s.Contains("SDK.DISPATCH_FAILED"))&&!logs.Exists(s=>s.Contains("PRIVATE")),"safe dispatch diagnosis");
  Console.WriteLine("PASS: throwing dispatch recovery, main-thread delivery, duplicate suppression and shutdown");
 }
}
