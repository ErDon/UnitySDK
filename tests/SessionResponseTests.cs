using System;
using System.Collections.Generic;
using ErDon.UnitySDK;
class SessionResponseTests
{
    sealed class Auth : IAuth {
        public LoginResult Result;
        public void Login(Action<LoginResult> callback) { callback(Result); }
        public void Dispose() { }
    }
    static void Check(bool value, string message) { if(!value)throw new Exception(message); }
    static void Main()
    {
        string id=Guid.NewGuid().ToString("D");
        var response=new SessionResponseData{errorCode="AUTH.CODE_REUSED",callId=id,session="must-not-succeed"};
        var result=SessionResponsePolicy.Evaluate(409,response);
        Check(!result.Success && result.Error.Code=="AUTH.CODE_REUSED" && result.Error.Retry==RetryPolicy.UserAction,"Replay diagnosis");
        Check(result.ServerCallId==id && result.Error.ServerCallId==id,"Server correlation retained");
        Check(SessionResponsePolicy.Evaluate(403,new SessionResponseData{errorCode="AUTH.APP_NOT_ALLOWED"}).Error.Retry==RetryPolicy.Never,"Binding configuration failure");
        Check(SessionResponsePolicy.Evaluate(502,new SessionResponseData{errorCode="AUTH.PLATFORM_UNAVAILABLE"}).Error.Retry==RetryPolicy.Delayed,"Upstream failure");
        Check(SessionResponsePolicy.Evaluate(429,null).Error.Code=="AUTH.RATE_LIMITED","Non JSON 429 still diagnosed");
        Check(SessionResponsePolicy.Evaluate(503,new SessionResponseData{errorCode="AUTH.CREDENTIAL_STORAGE_FAILED"}).Error.Retry==RetryPolicy.Delayed,"Credential storage failure requires fresh login later");
        Check(SessionResponsePolicy.Evaluate(401,new SessionResponseData{errorCode="AUTH.CREDENTIAL_STORAGE_FAILED"}).Error.Code=="AUTH.SERVER_REJECTED","Credential storage status is allowlisted");
        Check(SessionResponsePolicy.Evaluate(429,null,"37").Error.RetryAfterSeconds==37,"Server cooldown retained");
        foreach(var invalid in new[]{"-1","+2","1.5"," 30","999999999999","Wed, 21 Oct 2015 07:28:00 GMT"})
            Check(SessionResponsePolicy.Evaluate(429,null,invalid).Error.RetryAfterSeconds==null,"Invalid or unsupported cooldown ignored");
        Check(SessionResponsePolicy.Evaluate(200,new SessionResponseData{session="ok"},"30").Success,"Success unaffected by cooldown header");
        Check(SessionResponsePolicy.Evaluate(401,response).Error.Code=="AUTH.SERVER_REJECTED","Wrong code-status pair ignored");
        Check(!SessionResponsePolicy.Evaluate(200,response).Success,"Contradictory success response rejected");
        Check(!SessionResponsePolicy.Evaluate(200,new SessionResponseData{session=" "}).Success,"Blank session rejected");
        var good=SessionResponsePolicy.Evaluate(200,new SessionResponseData{session="private-session",callId=id});
        Check(good.Success && good.ServerCallId==id,"Successful response correlation");
        var hostile=SessionResponsePolicy.Evaluate(500,new SessionResponseData{errorCode="private-server-message",callId="secret\nforged-log"});
        Check(hostile.Error.Code=="AUTH.SERVER_REJECTED" && hostile.ServerCallId==null,"Unknown fields not echoed");
        var logs=new List<string>();
        SDK.Initialize(new SdkConfig{LogCallback=(level,line)=>logs.Add(line)},new Auth{Result=result},a=>a(),(level,line)=>{});
        LoginResult delivered=null; SDK.Login(r=>delivered=r);
        Check(delivered.Error.ServerCallId==id && delivered.CallId!=id,"Client and server identifiers distinct");
        Check(logs.Exists(line=>line.Contains("serverCallId="+id)),"Formatted SDK log includes server identifier");
        Check(!logs.Exists(line=>line.Contains("private-session") || line.Contains("must-not-succeed")),"Session stays out of logs");
        SDK.Shutdown();
        Console.WriteLine("PASS: response policy, safe server diagnostics and cross-service correlation");
    }
}
