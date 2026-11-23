#if !ERDON_EXCLUDE_SHARE || UNITY_EDITOR
using System;
using System.Globalization;
using System.Threading;
using TTSDK;

namespace ErDon.UnitySDK.TikTok
{
    /// <summary>Call after TT.InitSDK succeeds. Platform success does not prove recipient delivery.</summary>
    public sealed class TikTokShare : IShare
    {
        readonly Action<ShareAppMessageParam> dispatch;
        bool disposed;
        public TikTokShare() : this(TT.ShareAppMessage) { }
        internal TikTokShare(Action<ShareAppMessageParam> dispatch)
        {
            if (dispatch == null) throw new ArgumentNullException("dispatch");
            this.dispatch = dispatch;
        }
        public void Share(ShareRequest request, Action<ShareResult> callback)
        {
            if (callback == null) throw new ArgumentNullException("callback");
            if (disposed) { callback(ShareResult.Fail("SDK.DISPOSED", "TikTok share was disposed")); return; }
            if (request == null || string.IsNullOrWhiteSpace(request.Title)) {
                callback(ShareResult.Fail("SHARE.INVALID_ARGUMENT", "Share title must not be blank")); return;
            }
            int completed = 0;
            Action<ShareResult> finish = result => {
                if (disposed || Interlocked.Exchange(ref completed, 1) != 0) return;
                callback(result);
            };
            try {
                dispatch(new ShareAppMessageParam {
                    Title = request.Title, ImageUrl = request.ImageUrl, Query = request.Query,
                    Success = () => finish(ShareResult.Dispatched()),
                    Fail = error => finish(ShareResult.Fail(new SdkError("SHARE.PLATFORM_REJECTED", "TikTok rejected the share request",
                        error == null ? null : error.ErrorCode.ToString(CultureInfo.InvariantCulture), null)))
                    // Complete alone cannot establish success or failure; SDK timeout remains active.
                });
            } catch { finish(ShareResult.Fail("SHARE.PROVIDER_FAILED", "TikTok share invocation failed")); }
        }
        public void Dispose() { disposed = true; }
    }
}

#endif
