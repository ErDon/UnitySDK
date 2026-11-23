#if !ERDON_EXCLUDE_PAYMENTS || UNITY_EDITOR
using System;
using System.Globalization;
using System.Threading;
using TTSDK;

namespace ErDon.UnitySDK.TikTok
{
    /// <summary>Call on the Unity main thread after platform initialization. Never grants goods.</summary>
    public sealed class TikTokPaymentGateway : IPlatformPaymentGateway
    {
        readonly Action<TTPayParam> launch;
        bool disposed, busy;
        public TikTokPaymentGateway() : this(TT.Pay) { }
        internal TikTokPaymentGateway(Action<TTPayParam> launch)
        {
            if (launch == null) throw new ArgumentNullException("launch");
            this.launch = launch;
        }
        public void Launch(string tradeOrderId, Action<PaymentLaunchResult> callback)
        {
            if (callback == null) throw new ArgumentNullException("callback");
            if (disposed) { callback(PaymentLaunchResult.Fail(new SdkError("SDK.DISPOSED", "TikTok payment gateway was disposed"))); return; }
            if (string.IsNullOrWhiteSpace(tradeOrderId) || tradeOrderId.Length > 256 || tradeOrderId.Trim() != tradeOrderId || HasControl(tradeOrderId)) {
                callback(PaymentLaunchResult.Fail(new SdkError("PAY.INVALID_REQUEST", "A server-issued platform order ID is required"))); return;
            }
            if (busy) { callback(PaymentLaunchResult.Fail(new SdkError("PAY.IN_PROGRESS", "A payment panel is still pending"))); return; }
            busy = true;
            int completed = 0;
            Action<PaymentLaunchResult> finish = result => {
                if (disposed || Interlocked.Exchange(ref completed, 1) != 0) return;
                busy = false; callback(result);
            };
            try {
                launch(new TTPayParam {
                    trade_order_id = tradeOrderId,
                    success = result => finish(PaymentLaunchResult.Submitted(null)),
                    fail = error => finish(PaymentLaunchResult.Fail(new SdkError("PAY.PLATFORM_REJECTED", "TikTok payment flow failed; query server order status",
                        error == null ? null : error.ErrorCode.ToString(CultureInfo.InvariantCulture), null)))
                    // complete is not evidence of success. No receipt is fabricated.
                });
            } catch { finish(PaymentLaunchResult.Fail(new SdkError("PAY.LAUNCH_FAILED", "TikTok payment invocation failed; query server order status"))); }
        }
        static bool HasControl(string value) { foreach (char item in value) if (char.IsControl(item)) return true; return false; }
        public void Dispose() { disposed = true; }
    }
}
#endif
