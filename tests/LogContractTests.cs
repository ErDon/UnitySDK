using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using ErDon.UnitySDK;
class LogContractTests {
 sealed class Auth:IAuth { public void Login(Action<LoginResult> done){done(LoginResult.Ok("private-session"));}public void Dispose(){} }
 sealed class Runtime:IPlatformRuntime {public string Platform{get{return "Test";}}public bool IsReady{get{return false;}}public void Initialize(Action<SdkError> done){done(new SdkError("INIT.TEST_FAILURE","private-reason"));}public void Dispose(){} }
 sealed class Factory:IPlatformSdkProviderFactory {public string Platform{get{return "Test";}}public IAuth CreateAuth(Action<Action> post){throw new Exception("Should not create");}public IStorage CreateStorage(){throw new Exception("Should not create");}}
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
 static void Main(){
  var logs=new List<string>();int engineCalls=0;
  Action<SdkLogLevel,string> capture=(level,line)=>{Check(line.Contains("["+level+"]"),"level mismatch");logs.Add(line);};
  SDK.Initialize(new SdkConfig{Platform="Test\r\nInjected",LoginEnabled=false,LogCallback=capture},null,a=>a(),(l,s)=>engineCalls++);
  SDK.Login(r=>{});SDK.Share(null,r=>{});SDK.ShowRewardedAd(null,r=>{});SDK.Pay(null,r=>{});
  SDK.SetStorageSync("private-key","private-value");SDK.GetStorageSync("private-key");SDK.RemoveStorageSync("private-key");
  SDK.Request(null,r=>{});SDK.ReportEvent(null);SDK.ReportEvent(null,r=>{});SDK.Shutdown();
  SDK.Initialize(new SdkConfig{Platform="Test",LogCallback=capture},new Auth(),a=>a(),(l,s)=>engineCalls++);
  SDK.Login(r=>{throw new Exception("private-callback");});SDK.Shutdown();
  using(var startup=new SdkStartup(new SdkConfig{Platform="Test",LoginEnabled=false,LogCallback=capture},new Runtime(),new Factory(),a=>a(),(l,s)=>engineCalls++,r=>{throw new Exception("private-startup");})) {startup.Start();startup.Tick();}
  Check(engineCalls==0,"configured callback must own delivery");
  var thrown=new List<string>();var fallback=new List<string>();
  SDK.Initialize(new SdkConfig{LoginEnabled=false,LogCallback=(l,s)=>{thrown.Add(s);throw new Exception("private-sink");}},null,a=>a(),(l,s)=>fallback.Add(s));SDK.Login(r=>{});SDK.Shutdown();
  Check(thrown.SequenceEqual(fallback)&&thrown.Count>=3,"fallback must receive the identical SDK line");logs.AddRange(fallback);
  var direct=new List<string>();SDK.Initialize(new SdkConfig{LoginEnabled=false},null,a=>a(),(l,s)=>direct.Add(s));SDK.Login(r=>{});SDK.Shutdown();Check(direct.Count>=3,"engine default missing");logs.AddRange(direct);
  var ids=new HashSet<string>();var apis=new HashSet<string>();var stages=new Dictionary<string,HashSet<string>>();
  var pattern=new Regex(@"^(\S+) \[(Info|Warning|Error)\] platform=.*? interface=(\S+) callId=([0-9a-f]{32}) logId=([0-9a-f]{32}) traceId=([0-9a-f]{32}) stage=(\S+) params=(\{.*\}) result=(.*)$");
  foreach(var line in logs){
   Check(!line.Contains("\r")&&!line.Contains("\n")&&!line.Contains("private-"),"multiline or private data leaked");
   var m=pattern.Match(line);Check(m.Success,"invalid log envelope: "+line);
   DateTimeOffset time;Check(DateTimeOffset.TryParseExact(m.Groups[1].Value,"o",CultureInfo.InvariantCulture,DateTimeStyles.None,out time),"invalid timestamp");
   Check(ids.Add(m.Groups[5].Value),"logId reused");Check(m.Groups[4].Value==m.Groups[6].Value,"trace correlation lost");
   Check(new JavaScriptSerializer().DeserializeObject(m.Groups[8].Value) is Dictionary<string,object>,"parameters not JSON object");
   apis.Add(m.Groups[3].Value);var key=m.Groups[3].Value+":"+m.Groups[4].Value;
   if(!stages.ContainsKey(key))stages[key]=new HashSet<string>();stages[key].Add(m.Groups[7].Value);
  }
  foreach(var api in new[]{"Initialize","Login","Share","ShowRewardedAd","Pay","SetStorageSync","GetStorageSync","RemoveStorageSync","Request","ReportEvent","Shutdown","Startup"})Check(apis.Contains("SDK."+api),"missing interface log: "+api);
  foreach(var stagesForCall in stages.Values)if(stagesForCall.Contains("Start"))Check(stagesForCall.Contains("Complete"),"missing terminal correlation");
  Console.WriteLine("PASS: SDK log envelope, all capability interfaces, IDs, parameter JSON, privacy, startup and exact fallback delivery");
 }
}
