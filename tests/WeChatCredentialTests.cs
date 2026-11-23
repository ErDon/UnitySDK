using System;
using ErDon.UnitySDK;
using ErDon.UnitySDK.WeChat;
using WeChatWASM;
#if ERDON_WECHAT_STRING_ANALYTICS
using LoginFailure = WeChatWASM.LoginFailCallbackErr;
#else
using LoginFailure = WeChatWASM.RequestFailCallbackErr;
#endif

// WX's dispatch entry is stubbed; callback DTOs come from the actual pinned official DLL.
#if !ERDON_WECHAT_REAL_WX
namespace WeChatWASM { public class WX { public static void Login(LoginOption option) { throw new NotSupportedException(); } } }
#endif
class WeChatCredentialTests
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Main()
    {
        LoginOption pending = null;
        var provider = new WeChatCredentialProvider(option => pending = option, 4321);
        PlatformCredential credential = null;
        SdkError error = null;
        int calls = 0;
        Action<PlatformCredential, SdkError> capture = (c,e) => { credential=c; error=e; calls++; };
        provider.GetCredential(capture);
        Check(pending.timeout == 4321, "Timeout forwarded");
        pending.success(new LoginSuccessCallbackResult { code="secret-code" });
        pending.fail(new LoginFailure());
        Check(calls==1 && credential.Code=="secret-code" && credential.Platform=="WeChat" && error==null, "First callback wins");
        provider.GetCredential(capture);
        pending.success(new LoginSuccessCallbackResult { code=" " });
        Check(error.Code=="AUTH.INVALID_CREDENTIAL" && credential==null, "Empty credential rejected");
        provider.GetCredential(capture);
        pending.fail(new LoginFailure { errno=123, errMsg="secret-code" });
        Check(error.Code=="AUTH.PLATFORM_FAILED" && error.PlatformCode=="123" && error.PlatformMessage==null, "Numeric diagnosis without raw credential leak");
        provider.GetCredential(capture);
        provider.Dispose();
        int before=calls;
        pending.success(new LoginSuccessCallbackResult { code="late" });
        Check(calls==before, "Disposed callbacks suppressed");
        provider.GetCredential(capture);
        Check(error.Code=="SDK.DISPOSED", "Disposed request diagnosed");
        var throwing = new WeChatCredentialProvider(option => { throw new Exception("sensitive"); }, 1000);
        throwing.GetCredential(capture);
        Check(error.Code=="AUTH.PLATFORM_EXCEPTION" && !error.Reason.Contains("sensitive"), "Invocation exception isolated");
        Console.WriteLine("WECHAT_CREDENTIAL_TESTS_PASS (official DTOs; mocked dispatch, not device verification)");
    }
}
