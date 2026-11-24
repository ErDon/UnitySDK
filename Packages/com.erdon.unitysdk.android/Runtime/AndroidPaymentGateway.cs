#if !ERDON_EXCLUDE_PAYMENTS || UNITY_EDITOR
using System;
using UnityEngine;
namespace ErDon.UnitySDK.Android
{
    /// <summary>Server-prepared, channel-specific cashier payload. Never construct prices or signatures on the client.</summary>
    [Serializable]
    public sealed class AndroidPaymentParameters {
        public string orderId,parametersJson;
        public override string ToString(){return "AndroidPaymentParameters([REDACTED])";}
    }
    /// <summary>Compose with PreparedPaymentLauncher and VerifiedPayment; launching never grants items.</summary>
    public sealed class AndroidPaymentGateway : IPlatformPaymentGateway<AndroidPaymentParameters>
    {
        readonly IAndroidBridge bridge;
        public AndroidPaymentGateway(IAndroidBridge bridge){if(bridge==null)throw new ArgumentNullException("bridge");this.bridge=bridge;}
        public void Launch(AndroidPaymentParameters request,Action<PaymentLaunchResult> callback){
            if(callback==null)throw new ArgumentNullException("callback");
            if(request==null||string.IsNullOrWhiteSpace(request.orderId)||string.IsNullOrWhiteSpace(request.parametersJson)){
                callback(PaymentLaunchResult.Fail(new SdkError("PAY.INVALID_REQUEST","Server-prepared parameters required")));return;
            }
            bridge.Call("payment",JsonUtility.ToJson(request),r=>callback(r.success?PaymentLaunchResult.AwaitServerConfirmation():PaymentLaunchResult.Fail(r.Error)));
        }
        public void Dispose(){bridge.Dispose();}
    }
}
#endif
