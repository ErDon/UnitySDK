#if !ERDON_EXCLUDE_LOGIN || UNITY_EDITOR
using System;
using System.Threading;
using TTSDK;

namespace ErDon.UnitySDK.Douyin
{
    /// <summary>Call after official TT.InitSDK succeeds. A code is not a game session.</summary>
    public sealed class DouyinCredentialProvider : IPlatformCredentialProvider
    {
        readonly Action<Action<string, string, bool>, Action<string>> login;
        bool disposed;

        public DouyinCredentialProvider()
            : this((success, fail) => TT.Login((code, anonymousCode, isLogin) => success(code, anonymousCode, isLogin), message => fail(message), true)) { }

        internal DouyinCredentialProvider(Action<Action<string, string, bool>, Action<string>> login)
        {
            if (login == null) throw new ArgumentNullException("login");
            this.login = login;
        }

        public void GetCredential(Action<PlatformCredential, SdkError> callback)
        {
            if (callback == null) throw new ArgumentNullException("callback");
            if (disposed) { callback(null, new SdkError("SDK.DISPOSED", "Douyin provider was disposed")); return; }
            int completed = 0;
            Action<PlatformCredential, SdkError> finish = (credential, error) => {
                if (disposed || Interlocked.Exchange(ref completed, 1) != 0) return;
                callback(credential, error);
            };
            try {
                login((code, anonymousCode, isLogin) => {
                    if (!isLogin) {
                        finish(null, new SdkError("AUTH.NOT_LOGGED_IN", "Douyin did not return an authenticated account"));
                        return;
                    }
                    if (string.IsNullOrWhiteSpace(code))
                        finish(null, new SdkError("AUTH.INVALID_CREDENTIAL", "Douyin returned an empty login code"));
                    else finish(new PlatformCredential("Douyin", code), null);
                }, message => {
                    // This API exposes only free text. Do not guess error codes or leak credentials.
                    finish(null, new SdkError("AUTH.PLATFORM_FAILED", "Douyin login failed"));
                });
            }
            catch { finish(null, new SdkError("AUTH.PLATFORM_EXCEPTION", "Douyin login invocation failed")); }
        }

        public void Dispose() { disposed = true; }
    }
}

#endif
