using System;
using System.Collections.Generic;
using ErDon.UnitySDK;
class ResultSnapshotTests {
 sealed class Provider:IAuth,IShare,IPay,IStorage {
  public SdkError Error=new SdkError("TEST.FAIL","reason").WithRetry(RetryPolicy.Delayed,5);
  public LoginResult LoginResult; public ShareResult ShareResult; public PaymentResult PaymentResult;
  public StorageResult StorageResult=StorageResult.Fail("STORAGE.TEST","reason");
  public Provider(){LoginResult=LoginResult.Fail(Error);ShareResult=ShareResult.Fail(Error);PaymentResult=PaymentResult.Fail(Error);}
  public void Login(Action<LoginResult> cb){cb(LoginResult);}
  public void Share(ShareRequest r,Action<ShareResult> cb){cb(ShareResult);}
  public void Pay(PaymentRequest r,Action<PaymentResult> cb){cb(PaymentResult);}
  public StorageResult Set(string k,string v){return StorageResult;} public StorageResult Get(string k){return StorageResult;} public StorageResult Remove(string k){return StorageResult;}
  public void Dispose(){}
 }
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);}
 static void Main(){
  var p=new Provider();var q=new Queue<Action>();
  SDK.Initialize(new SdkConfig{StorageEnabled=true,ShareEnabled=true,PayEnabled=true},p,a=>q.Enqueue(a),(l,s)=>{},p,p,p);
  LoginResult first=null,second=null;ShareResult share=null;PaymentResult pay=null;
  SDK.Login(r=>first=r);p.Error.WithRetry(RetryPolicy.Never);q.Dequeue()();
  Check(first.Error.Retry==RetryPolicy.Delayed&&first.Error.RetryAfterSeconds==5,"snapshot at acceptance before dispatch");
  string id=first.CallId;SDK.Login(r=>second=r);q.Dequeue()();
  Check(first.CallId==id&&first.Error.CallId==id&&second.CallId!=id,"reused login keeps history");
  SDK.Share(new ShareRequest("title"),r=>share=r);q.Dequeue()();
  SDK.Pay(new PaymentRequest("session","product","id"),r=>pay=r);q.Dequeue()();
  Check(first.Error.Operation=="SDK.Login"&&share.Error.Operation=="SDK.Share"&&pay.Error.Operation=="SDK.Pay","shared error remains per operation");
  Check(p.Error.CallId==null&&p.LoginResult.CallId==null&&p.ShareResult.CallId==null&&p.PaymentResult.CallId==null,"provider objects not mutated");
  var stored=SDK.SetStorageSync("k","v");var storedId=stored.CallId;var read=SDK.GetStorageSync("k");
  Check(stored.CallId==storedId&&stored.Error.Operation=="SDK.SetStorageSync"&&read.Error.Operation=="SDK.GetStorageSync","storage history stable");
  Check(p.StorageResult.CallId==null&&p.StorageResult.Error.CallId==null,"storage provider not mutated");
  share.Error.WithRetry(RetryPolicy.UserAction);Check(pay.Error.Retry==RetryPolicy.Never,"caller mutation isolated");
  SDK.Shutdown();Console.WriteLine("PASS: stable result/error snapshots across reused providers and queued callbacks");
 }
}
