using System;
using ErDon.UnitySDK;
class AuthPipelineTests
{
 class Credentials:IPlatformCredentialProvider{
  public Action<PlatformCredential,SdkError> Pending;
  public void GetCredential(Action<PlatformCredential,SdkError> callback){Pending=callback;}
  public void Dispose(){}
 }
 class Backend:ISessionExchange{
  public int Calls;public Action<LoginResult> Pending;
  public void Exchange(PlatformCredential c,Action<LoginResult> callback){Calls++;Pending=callback;}
  public void Dispose(){}
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Main(){
  var p=new Credentials();var b=new Backend();var auth=new VerifiedAuth(p,b,a=>a());LoginResult result=null;int calls=0;
  auth.Login(r=>{result=r;calls++;});p.Pending(new PlatformCredential("WeChat","secret-code"),null);
  Check(result==null,"platform code must not complete login");
  p.Pending(new PlatformCredential("WeChat","duplicate"),null);Check(b.Calls==1,"duplicate code exchange");
  b.Pending(LoginResult.Ok("backend-session"));b.Pending(LoginResult.Ok("duplicate-session"));Check(calls==1 && result.Session=="backend-session","complete once");
  auth.Login(r=>result=r);p.Pending(null,new SdkError("AUTH.PERMISSION_DENIED","denied"));Check(result.Error.Stage=="PlatformAuthorization","auth stage");
  auth.Login(r=>result=r);p.Pending(new PlatformCredential("WeChat","code"),null);b.Pending(LoginResult.Ok(""));Check(result.Error.Code=="AUTH.INVALID_SESSION","empty session");
  auth.Login(r=>calls++);auth.Dispose();p.Pending(new PlatformCredential("WeChat","late"),null);Check(calls==1,"dispose suppress late");
  Check(!new PlatformCredential("WeChat","secret").ToString().Contains("secret"),"credential ToString");
  Console.WriteLine("PASS: backend verification required, duplicate guards, stages, empty session, disposal, redaction");
 }
}
