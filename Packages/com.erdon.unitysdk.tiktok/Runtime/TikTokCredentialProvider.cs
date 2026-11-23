#if !ERDON_EXCLUDE_LOGIN || UNITY_EDITOR
using System;
using System.Threading;
using TTSDK;

namespace ErDon.UnitySDK.TikTok
{
    /// <summary>Call after official TT.InitSDK succeeds. A code is not a game session.</summary>
    public sealed class TikTokCredentialProvider : IPlatformCredentialProvider
    {
        readonly Action<Action<string>, Action<string>> login;
        bool disposed;

        public TikTokCredentialProvider()
            : this((success, fail) => TT.Login(code => success(code), message => fail(message))) { }

        internal TikTokCredentialProvider(Action<Action<string>, Action<string>> login)
        {
            if (login == null) throw new ArgumentNullException("login");
            this.login = login;
        }

        public void GetCredential(Action<PlatformCredential, SdkError> callback)
        {
            if (callback == null) throw new ArgumentNullException("callback");
            if (disposed) { callback(null, new SdkError("SDK.DISPOSED", "TikTok provider was disposed")); return; }
            int completed = 0;
            Action<PlatformCredential, SdkError> finish = (credential, error) => {
                if (disposed || Interlocked.Exchange(ref completed, 1) != 0) return;
                callback(credential, error);
            };
            try {
                login(code => {
                    if (string.IsNullOrWhiteSpace(code))
                        finish(null, new SdkError("AUTH.INVALID_CREDENTIAL", "TikTok returned an empty login code"));
                    else finish(new PlatformCredential("TikTok", code), null);
                }, message => {
                    // This API exposes only free text. Do not guess error codes or leak credentials.
                    finish(null, new SdkError("AUTH.PLATFORM_FAILED", "TikTok login failed"));
                });
            }
            catch { finish(null, new SdkError("AUTH.PLATFORM_EXCEPTION", "TikTok login invocation failed")); }
        }

        public void Dispose() { disposed = true; }
    }
}

#endif
