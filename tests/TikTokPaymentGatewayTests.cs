using System;
using ErDon.UnitySDK;
using ErDon.UnitySDK.TikTok;
using TTSDK;
class TikTokPaymentGatewayTests
{
    static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    static int Main() { try { Run(); return 0; } catch (Exception error) { Console.WriteLine(error); return 1; } }
    static void Run()
    {
        TTPayParam sent = null; int calls = 0, callbacks = 0; PaymentLaunchResult result = null;
        var gateway = new TikTokPaymentGateway(p => { sent = p; calls++; });
        gateway.Launch("server-trade", r => { result = r; callbacks++; });
        Check(sent.trade_order_id == "server-trade" && result == null, "ID mapped, waiting");
        Check(sent.complete == null, "Complete must not fabricate success");
        gateway.Launch("second", r => result = r);
        Check(result.Error.Code == "PAY.IN_PROGRESS" && calls == 1, "Concurrent panel rejected");
        sent.success(null); sent.fail(null);
        Check(callbacks == 1 && result.Error == null && result.Evidence == null, "First wins; no fabricated receipt");
        gateway.Launch("next", r => result = r);
        var error = (ErrorInfo)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(ErrorInfo));
        error.ErrorCode = 123; error.ErrMsg = "private server details"; sent.fail(error);
        Check(result.Error.Code == "PAY.PLATFORM_REJECTED" && result.Error.PlatformCode == "123" && result.Error.PlatformMessage == null, "Only structured code retained");
        foreach (string id in new[] { "", " bad", "bad\n", new string('x',257) }) {
            gateway.Launch(id, r => result = r); Check(result.Error.Code == "PAY.INVALID_REQUEST", "Invalid ID");
        }
        Check(calls == 2, "Invalid calls never dispatch");
        gateway.Launch("last", r => callbacks++); gateway.Dispose(); sent.success(null);
        Check(callbacks == 1, "Late callback suppressed");
        gateway.Launch("last", r => result = r); Check(result.Error.Code == "SDK.DISPOSED", "Disposed rejected");
        new TikTokPaymentGateway(p => { throw new Exception("private"); }).Launch("trade", r => result = r);
        Check(result.Error.Code == "PAY.LAUNCH_FAILED" && !result.Error.Reason.Contains("private"), "Exception sanitized");
        Console.WriteLine("TIKTOK_PAYMENT_GATEWAY_TESTS_PASS (official types compiled; no real payment)");
    }
}
