#if !ERDON_EXCLUDE_LOGIN || UNITY_EDITOR
using System;
using System.Globalization;
using System.Threading;
using WeChatWASM;

namespace ErDon.UnitySDK.WeChat
{
    /// <summary>Only obtains a temporary code. Compose with VerifiedAuth for server verification.</summary>
    public sealed class WeChatCredentialProvider : IPlatformCredentialProvider
    {
        readonly Action<LoginOption> login;
        readonly int timeoutMilliseconds;
        bool disposed;

        public WeChatCredentialProvider(int timeoutMilliseconds = 10000)
            : this(WX.Login, timeoutMilliseconds) { }

        // Same-assembly test seam; games use the public constructor.
        internal WeChatCredentialProvider(Action<LoginOption> login, int timeoutMilliseconds)
        {
            if (login == null) throw new ArgumentNullException("login");
            if (timeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException("timeoutMilliseconds");
            this.login = login;
            this.timeoutMilliseconds = timeoutMilliseconds;
        }

        public void GetCredential(Action<PlatformCredential, SdkError> callback)
        {
            if (callback == null) throw new ArgumentNullException("callback");
            if (disposed) { callback(null, new SdkError("SDK.DISPOSED", "WeChat provider was disposed")); return; }
            int completed = 0;
            Action<PlatformCredential, SdkError> finish = (credential, error) => {
                if (disposed || Interlocked.Exchange(ref completed, 1) != 0) return;
                callback(credential, error);
            };
            var options = new LoginOption {
                timeout = timeoutMilliseconds,
                success = result => {
                    if (result == null || string.IsNullOrWhiteSpace(result.code))
                        finish(null, new SdkError("AUTH.INVALID_CREDENTIAL", "WeChat returned an empty login code"));
                    else finish(new PlatformCredential("WeChat", result.code), null);
                },
                fail = error => {
                    // Preserve the structured numeric error. Arbitrary errMsg may contain credentials.
                    string code = error == null ? null : Convert.ToString(error.errno, CultureInfo.InvariantCulture);
                    finish(null, new SdkError("AUTH.PLATFORM_FAILED", "WeChat login failed; inspect PlatformCode", code, null));
                }
            };
            try { login(options); }
            catch { finish(null, new SdkError("AUTH.PLATFORM_EXCEPTION", "WeChat login invocation failed")); }
        }

        public void Dispose() { disposed = true; }
    }
}

#endif
