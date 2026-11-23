#if !ERDON_EXCLUDE_SHARE || UNITY_EDITOR
using System;
using WeChatWASM;

namespace ErDon.UnitySDK.WeChat
{
    /// <summary>Starts the official share UI. No claim of user sending or recipient delivery.</summary>
    public sealed class WeChatShare : IShare
    {
        readonly Action<ShareAppMessageOption> dispatch;
        bool disposed;
        public WeChatShare() : this(WX.ShareAppMessage) { }
        internal WeChatShare(Action<ShareAppMessageOption> dispatch)
        {
            if (dispatch == null) throw new ArgumentNullException("dispatch");
            this.dispatch = dispatch;
        }
        public void Share(ShareRequest request, Action<ShareResult> callback)
        {
            if (callback == null) throw new ArgumentNullException("callback");
            if (disposed) { callback(ShareResult.Fail("SDK.DISPOSED", "WeChat share was disposed")); return; }
            if (request == null || string.IsNullOrWhiteSpace(request.Title)) {
                callback(ShareResult.Fail("SHARE.INVALID_ARGUMENT", "Share title must not be blank")); return;
            }
            ShareResult result;
            try {
                dispatch(new ShareAppMessageOption { title = request.Title, imageUrl = request.ImageUrl, query = request.Query });
                // The pinned official option has no success/fail callbacks.
                result = ShareResult.Dispatched();
            } catch { result = ShareResult.Fail("SHARE.PROVIDER_FAILED", "WeChat share invocation failed"); }
            if (!disposed) callback(result);
        }
        public void Dispose() { disposed = true; }
    }
}

#endif
