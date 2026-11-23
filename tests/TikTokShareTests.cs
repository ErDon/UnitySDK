using System;
using ErDon.UnitySDK;
using ErDon.UnitySDK.TikTok;
using TTSDK;
class TikTokShareTests
{
    static void Check(bool value,string reason) { if(!value)throw new Exception(reason); }
    static ErrorInfo Error()
    {
        // Official SDK constructs errors internally; this test supplies a callback fixture.
        var error=(ErrorInfo)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(ErrorInfo));
        error.ErrorCode=123;error.ErrMsg="private details";return error;
    }
    static int Main() { try { Run(); return 0; } catch(Exception error) { Console.WriteLine(error.GetType().FullName); Console.WriteLine(error.Message); return 1; } }
    static void Run()
    {
        ShareAppMessageParam sent=null;int invoked=0,callbacks=0;
        var provider=new TikTokShare(p=>{sent=p;invoked++;});ShareResult result=null;
        provider.Share(new ShareRequest("title","https://game.example/image.png","scene=a%26b"),r=>{result=r;callbacks++;});
        Check(result==null,"Must wait for platform callback");
        Check(sent.Title=="title"&&sent.ImageUrl=="https://game.example/image.png"&&sent.Query=="scene=a%26b","Field mapping");
        sent.Success();sent.Fail(Error());
        Check(callbacks==1&&result.Success,"First signal wins");
        provider.Share(new ShareRequest("title"),r=>result=r);
        Check(sent.ImageUrl==null&&sent.Query==null,"Optional fields omitted");
        sent.Fail(Error());
        Check(result.Error.Code=="SHARE.PLATFORM_REJECTED"&&result.Error.PlatformCode=="123"&&result.Error.PlatformMessage==null,"Structured error only");
        provider.Share(new ShareRequest("title"),r=>result=r);sent.Fail(null);
        Check(result.Error.PlatformCode==null,"Missing upstream error handled");
        provider.Share(null,r=>result=r);Check(result.Error.Code=="SHARE.INVALID_ARGUMENT"&&invoked==3,"Invalid arguments rejected");
        provider.Share(new ShareRequest("title"),r=>callbacks++);provider.Dispose();sent.Success();
        Check(callbacks==1,"Disposed callback suppressed");
        provider.Share(new ShareRequest("title"),r=>result=r);Check(result.Error.Code=="SDK.DISPOSED"&&invoked==4,"Disposed call rejected");
        new TikTokShare(p=>{throw new Exception("private");}).Share(new ShareRequest("title"),r=>result=r);
        Check(result.Error.Code=="SHARE.PROVIDER_FAILED"&&!result.Error.Reason.Contains("private"),"Dispatch exception sanitized");
        Console.WriteLine("TIKTOK_SHARE_TESTS_PASS (official types/entry compiled; dispatch injected)");
    }
}

