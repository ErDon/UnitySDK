using System;
using ErDon.UnitySDK;
using ErDon.UnitySDK.Douyin;
using TTSDK.UNBridgeLib.LitJson;
class DouyinShareTests
{
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);}
    static void Main(){
        JsonData sent=null; Action success=null,cancel=null; Action<string> fail=null;
        int invocations=0,completed=0; ShareResult result=null;
        var provider=new DouyinShare((j,s,f,c)=>{sent=j;success=s;fail=f;cancel=c;invocations++;});
        Action<ShareResult> capture=r=>{result=r;completed++;};
        provider.Share(new ShareRequest("中文\"title","https://game.example/a.png","scene=a%26b"),capture);
        Check(result==null,"wait for callback");
        Check((string)sent["title"]=="中文\"title"&&(string)sent["query"]=="scene=a%26b"&&(string)sent["imageUrl"]=="https://game.example/a.png","field mapping");
        Check((string)JsonMapper.ToObject(sent.ToJson())["title"]=="中文\"title","JSON escaping");
        success();cancel();fail("late");Check(completed==1&&result.Success,"first callback wins");
        provider.Share(new ShareRequest("title"),capture);Check(sent.Count==1,"optional fields omitted");
        cancel();success();Check(completed==2&&result.Error.Code=="SHARE.CANCELLED","platform cancellation");
        provider.Share(new ShareRequest("title"),capture);fail("private token");
        Check(result.Error.Code=="SHARE.PLATFORM_REJECTED"&&result.Error.PlatformMessage==null&&!result.Error.Reason.Contains("private"),"failure redacted");
        provider.Share(null,capture);Check(result.Error.Code=="SHARE.INVALID_ARGUMENT"&&invocations==3,"invalid rejected");
        provider.Share(new ShareRequest("title"),capture);provider.Dispose();int before=completed;
        success();cancel();Check(completed==before,"disposed callbacks ignored");
        provider.Share(new ShareRequest("title"),capture);Check(result.Error.Code=="SDK.DISPOSED","disposed call rejected");
        new DouyinShare((j,s,f,c)=>{throw new Exception("secret");}).Share(new ShareRequest("title"),capture);
        Check(result.Error.Code=="SHARE.PROVIDER_FAILED","safe invocation failure");
        new DouyinShare().Dispose();
        Console.WriteLine("DOUYIN_SHARE_TESTS_PASS (official entry compiled; injected callbacks, not device sharing)");
    }
}
