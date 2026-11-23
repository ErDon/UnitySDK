using System;
using System.Threading;
using TTSDK;
using TTSDK.UNBridgeLib.LitJson;

namespace ErDon.UnitySDK.Douyin
{
    /// <summary>Initialize TTSDK first. Success does not prove delivery or qualify a reward.</summary>
    public sealed class DouyinShare : IShare
    {
        readonly Action<JsonData, Action, Action<string>, Action> dispatch;
        bool disposed;
        public DouyinShare() : this((json, success, fail, cancel) =>
            TT.ShareAppMessage(json, result => success(), message => fail(message), () => cancel())) { }
        internal DouyinShare(Action<JsonData, Action, Action<string>, Action> dispatch)
        {
            if (dispatch == null) throw new ArgumentNullException("dispatch");
            this.dispatch = dispatch;
        }
        public void Share(ShareRequest request, Action<ShareResult> callback)
        {
            if (callback == null) throw new ArgumentNullException("callback");
            if (disposed) { callback(ShareResult.Fail("SDK.DISPOSED", "Douyin share was disposed")); return; }
            if (request == null || string.IsNullOrWhiteSpace(request.Title)) {
                callback(ShareResult.Fail("SHARE.INVALID_ARGUMENT", "Share title must not be blank")); return;
            }
            int completed = 0;
            Action<ShareResult> finish = result => {
                if (disposed || Interlocked.Exchange(ref completed, 1) != 0) return;
                callback(result);
            };
            try {
                var json = new JsonData();
                json["title"] = request.Title;
                if (request.ImageUrl != null) json["imageUrl"] = request.ImageUrl;
                if (request.Query != null) json["query"] = request.Query;
                dispatch(json, () => finish(ShareResult.Dispatched()),
                    message => finish(ShareResult.Fail("SHARE.PLATFORM_REJECTED", "Douyin rejected the share request")),
                    () => finish(ShareResult.Fail("SHARE.CANCELLED", "Douyin share was cancelled")));
                // Failure is free text; do not guess codes or expose its potentially sensitive contents.
            } catch { finish(ShareResult.Fail("SHARE.PROVIDER_FAILED", "Douyin share invocation failed")); }
        }
        public void Dispose() { disposed = true; }
    }
}
