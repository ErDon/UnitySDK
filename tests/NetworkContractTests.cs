using System;
using System.Collections.Generic;
using ErDon.UnitySDK;
class NetworkContractTests {
 static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
 static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Invalid request accepted");}
 static void Main(){
  var bytes=new byte[]{1,2};var headers=new Dictionary<string,string>{{"Authorization","private"}};
  var request=new NetworkRequest("https://example.com/api","post",bytes,headers);
  bytes[0]=9;headers["Authorization"]="changed";request.Body[0]=8;request.Headers["Authorization"]="copy";
  Check(request.Method=="POST"&&request.Body[0]==1&&request.Headers["authorization"]=="private","immutable request snapshot");
  Reject(()=>new NetworkRequest("http://example.com"));Reject(()=>new NetworkRequest("https://user:pass@example.com"));
  Reject(()=>new NetworkRequest("https://example.com/#x"));Reject(()=>new NetworkRequest("https://example.com","CONNECT"));
  Reject(()=>new NetworkRequest("https://example.com","GET",new byte[]{1}));
  Reject(()=>new NetworkRequest("https://example.com",headers:new Dictionary<string,string>{{"X-Test","x\r\nInjected: yes"}}));
  Reject(()=>new NetworkRequest("https://example.com",headers:new Dictionary<string,string>{{"X-Test","one"},{"x-test","two"}}));
  Reject(()=>new NetworkRequest("https://example.com",timeoutSeconds:0));Reject(()=>new NetworkRequest("https://example.com",maxResponseBytes:16777217));
  var response=NetworkResult.Response(404,new byte[]{4},headers);response.Body[0]=8;response.Headers.Clear();
  Check(response.Success&&!response.IsHttpSuccess&&response.StatusCode==404&&response.Body[0]==4&&response.Headers.Count==1,"HTTP failure differs from transport failure and copies data");
  Check(NetworkResult.Response(204,null,null).IsHttpSuccess,"204 accepted");
  Check(!NetworkResult.Fail("NETWORK.UNAVAILABLE","failed").Success,"transport error");
  using(var buffer=new BoundedResponseBuffer(4)){
   Check(buffer.Append(new byte[]{1,2},2)&&buffer.Append(new byte[]{3,4},2),"exact byte limit");
   Check(!buffer.Append(new byte[]{5},1)&&buffer.Exceeded&&buffer.ToArray().Length==4,"oversized chunk not retained");
   Check(!buffer.Append(new byte[0],0),"overflow sticky");
  }
  Console.WriteLine("PASS: network request validation, snapshots, HTTP semantics and streaming response limit");
 }
}
