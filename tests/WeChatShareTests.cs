using System;
using ErDon.UnitySDK;
using ErDon.UnitySDK.WeChat;
using WeChatWASM;
class WeChatShareTests
{
    static void Check(bool value, string message) { if(!value)throw new Exception(message); }
    static void Main()
    {
        ShareAppMessageOption sent=null;int calls=0;
        var provider=new WeChatShare(option=>{sent=option;calls++;});
        ShareResult result=null;
        provider.Share(new ShareRequest("title","https://game.example/share.png","scene=a%26b"),r=>result=r);
        Check(result.Success && calls==1,"Dispatch result");
        Check(sent.title=="title" && sent.imageUrl=="https://game.example/share.png" && sent.query=="scene=a%26b","Official field mapping preserves encoded query");
        provider.Share(new ShareRequest("title"),r=>result=r);
        Check(result.Success&&sent.imageUrl==null&&sent.query==null,"Optional fields omitted");
        provider.Share(null,r=>result=r);Check(result.Error.Code=="SHARE.INVALID_ARGUMENT"&&calls==2,"Invalid input not dispatched");
        provider.Dispose();provider.Share(new ShareRequest("title"),r=>result=r);
        Check(result.Error.Code=="SDK.DISPOSED"&&calls==2,"Disposed provider not dispatched");
        var failing=new WeChatShare(option=>{throw new Exception("private share content");});
        failing.Share(new ShareRequest("title"),r=>result=r);
        Check(result.Error.Code=="SHARE.PROVIDER_FAILED"&&!result.Error.Reason.Contains("private"),"Error sanitized");
        int callbacks=0;
        var good=new WeChatShare(option=>{});
        try { good.Share(new ShareRequest("title"),r=>{callbacks++;throw new InvalidOperationException();}); }
        catch(InvalidOperationException) { }
        Check(callbacks==1,"Consumer exception does not trigger second callback");
        Console.WriteLine("WECHAT_SHARE_TESTS_PASS (official types/entry compiled; dispatch injected)");
    }
}
