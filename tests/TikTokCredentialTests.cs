using System;
using ErDon.UnitySDK;
using ErDon.UnitySDK.TikTok;

class TikTokCredentialTests
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Main()
    {
        Action<string> success = null, fail = null;
        var provider = new TikTokCredentialProvider((s,f) => { success=s; fail=f; });
        PlatformCredential credential = null;
        SdkError error = null;
        int calls=0;
        Action<PlatformCredential,SdkError> capture=(c,e)=>{credential=c;error=e;calls++;};
        provider.GetCredential(capture);
        success("sensitive-code"); fail("sensitive-message");
        Check(calls==1 && credential.Code=="sensitive-code" && credential.Platform=="TikTok", "First callback wins");
        provider.GetCredential(capture); success(" ");
        Check(error.Code=="AUTH.INVALID_CREDENTIAL", "Empty credential rejected");
        provider.GetCredential(capture); fail("sensitive-message");
        Check(error.Code=="AUTH.PLATFORM_FAILED" && error.PlatformMessage==null && error.PlatformCode==null, "Raw message omitted, no invented code");
        provider.GetCredential(capture); provider.Dispose(); int before=calls; success("late");
        Check(calls==before, "Late callback suppressed");
        provider.GetCredential(capture); Check(error.Code=="SDK.DISPOSED", "Disposed request diagnosed");
        new TikTokCredentialProvider((s,f)=>{throw new Exception("secret");}).GetCredential(capture);
        Check(error.Code=="AUTH.PLATFORM_EXCEPTION" && !error.Reason.Contains("secret"), "Exception sanitized");
        Console.WriteLine("TIKTOK_CREDENTIAL_TESTS_PASS (official entry signature compiled; mocked dispatch, not device verification)");
    }
}
