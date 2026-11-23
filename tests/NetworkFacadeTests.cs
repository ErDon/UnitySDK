using System;
using System.Collections.Generic;
using System.Threading;
using ErDon.UnitySDK;
class NetworkFacadeTests {
 sealed class Operation:INetworkOperation {public int Cancels;public Action Cancelled;public void Cancel(){Cancels++;if(Cancelled!=null)Cancelled();}}
 sealed class Provider:INetwork {
  public readonly List<Action<NetworkResult>> Callbacks=new List<Action<NetworkResult>>();
  public readonly List<Operation> Operations=new List<Operation>();public int Disposes;
  public INetworkOperation Send(NetworkRequest request,Action<NetworkResult> cb){Callbacks.Add(cb);var op=new Operation{Cancelled=()=>cb(NetworkResult.Fail("LATE","late"))};Operations.Add(op);return op;}
  public void Dispose(){Disposes++;}
 }
 sealed class Factory:ISdkProviderFactory,INetworkProviderFactory {
  public int Created;public Provider Provider=new Provider();public INetwork CreateNetwork(){Created++;return Provider;}
  public IAuth CreateAuth(Action<Action> p){throw new Exception();}public IStorage CreateStorage(){throw new Exception();}
 }
 static void Check(bool v,string reason){if(!v)throw new Exception(reason);}
 static void Main(){
  var logs=new List<string>();var factory=new Factory();NetworkResult first=null,second=null;int firstCount=0;
  var request=new NetworkRequest("https://example.com/private?token=PRIVATE",timeoutSeconds:1);
  SDK.InitializeWithFactory(new SdkConfig{LoginEnabled=false},factory,action=>action(),(l,s)=>logs.Add(s));
  SDK.Request(request,r=>first=r);Check(first.Error.Code=="MODULE.DISABLED"&&factory.Created==0,"disabled zero construction");
  SDK.InitializeWithFactory(new SdkConfig{LoginEnabled=false,NetworkEnabled=true},factory,action=>action(),(l,s)=>logs.Add(s));
  var a=SDK.Request(request,r=>{first=r;firstCount++;});var b=SDK.Request(request,r=>second=r);
  a.Cancel();a.Cancel();Check(first.Error.Code=="NETWORK.CANCELLED"&&firstCount==1&&factory.Provider.Operations[0].Cancels==1,"cancel first terminal only");
  factory.Provider.Callbacks[1](NetworkResult.Response(404,new byte[]{1},null));
  Check(second.Success&&!second.IsHttpSuccess&&second.CallId!=first.CallId&&second.TraceId==second.CallId,"independent concurrent requests and status");
  b.Cancel();Check(factory.Provider.Operations[1].Cancels==0,"completed handle inert");
  SDK.Request(request,r=>first=r);Thread.Sleep(1050);SDK.Tick();
  Check(first.Error.Code=="NETWORK.TIMEOUT"&&first.Error.CallId==first.CallId&&factory.Provider.Operations[2].Cancels==1,"timeout abort and correlation");
  int stale=0;SDK.Request(request,r=>stale++);SDK.Shutdown();factory.Provider.Callbacks[3](NetworkResult.Response(200,null,null));
  Check(stale==0&&factory.Provider.Disposes==1&&factory.Provider.Operations[3].Cancels==1,"shutdown abort and stale suppression");
  var broken=new Provider();SDK.Initialize(new SdkConfig{LoginEnabled=false,NetworkEnabled=true},null,x=>{throw new Exception();},(l,s)=>logs.Add(s),networkProvider:broken);
  int recovered=0;SDK.Request(request,r=>recovered++);broken.Callbacks[0](NetworkResult.Response(200,null,null));Check(recovered==0,"throwing dispatch does not deliver inline");SDK.Tick();Check(recovered==1,"dispatch recovery");SDK.Shutdown();
  Check(!string.Join("",logs).Contains("PRIVATE")&&logs.Exists(s=>s.Contains("interface=SDK.Request")&&s.Contains("traceId=")),"network log privacy");
  Console.WriteLine("PASS: concurrent network facade, cancellation, timeout, ownership, dispatch and redacted diagnostics");
 }
}

