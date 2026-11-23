using System;
using ErDon.UnitySDK;
using ErDon.UnitySDK.Douyin;

// Documentation contract only. This is not an official SDK binary and is never shipped.
#if !OFFICIAL_DOUYIN
namespace TTSDK {
    public static class TT {
        public static bool Force;
        public static void Login(Action<string,string,bool> success, Action<string> fail, bool forceLogin=true) {
            Force=forceLogin; success("test-code","anonymous-code",true);
        }
    }
}
#endif
class DouyinCredentialTests
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static void Main(){
        Action<string,string,bool> success=null; Action<string> fail=null;
        var provider=new DouyinCredentialProvider((s,f)=>{success=s;fail=f;});
        PlatformCredential credential=null;SdkError error=null;int count=0;
        Action<PlatformCredential,SdkError> capture=(c,e)=>{credential=c;error=e;count++;};
        provider.GetCredential(capture);success("", "anonymous", false);
        Check(error.Code=="AUTH.NOT_LOGGED_IN" && credential==null,"anonymous identity not accepted");
        provider.GetCredential(capture);success("", "anonymous", true);
        Check(error.Code=="AUTH.INVALID_CREDENTIAL","authenticated state still needs code");
        provider.GetCredential(capture);success("valid", "anonymous", true);fail("late-sensitive-text");
        Check(credential.Code=="valid" && credential.Platform=="Douyin" && count==3,"first callback wins");
        provider.GetCredential(capture);fail("private-server-message");
        Check(error.Code=="AUTH.PLATFORM_FAILED" && error.PlatformMessage==null,"failure text omitted");
        provider.GetCredential(capture);provider.Dispose();int before=count;success("late",null,true);
        Check(count==before,"disposed callback ignored");
        provider.GetCredential(capture);Check(error.Code=="SDK.DISPOSED","disposed request rejected");
#if !OFFICIAL_DOUYIN
        new DouyinCredentialProvider().GetCredential(capture);
        Check(TTSDK.TT.Force && credential.Code=="test-code","default bridge requests account login");
        Console.WriteLine("DOUYIN_DOCUMENTED_CONTRACT_TESTS_PASS (test stub only; official SDK compile still required)");
#else
        // Compile the real bridge, but do not invoke native Unity APIs outside a Player.
        new DouyinCredentialProvider().Dispose();
        Console.WriteLine("DOUYIN_OFFICIAL_BINARY_CONTRACT_TESTS_PASS (injected callbacks; no device login)");
#endif
    }
}
