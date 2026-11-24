using System;
using System.Globalization;

namespace ErDon.UnitySDK
{
    [Serializable]
    public sealed class SessionResponseData
    {
        public string session;
        public string errorCode;
        public string callId;
    }

    // The same policy is used by Unity transport and standalone contract tests.
    public static class SessionResponsePolicy
    {
        public static LoginResult Evaluate(long status, SessionResponseData response, string retryAfter = null)
        {
            LoginResult result;
            if (status == 200) {
                result = response == null || !string.IsNullOrEmpty(response.errorCode) || string.IsNullOrWhiteSpace(response.session)
                    ? LoginResult.Fail("AUTH.INVALID_SESSION", "Session response is invalid")
                    : LoginResult.Ok(response.session);
            } else {
                var error = MapError(status, response == null ? null : response.errorCode);
                int seconds;
                if (status == 429 && int.TryParse(retryAfter, NumberStyles.None, CultureInfo.InvariantCulture, out seconds) && seconds > 0)
                    error.WithRetry(RetryPolicy.Delayed, seconds);
                result = LoginResult.Fail(error);
            }
            return result.WithServerCallId(response == null ? null : response.callId);
        }

        static SdkError MapError(long status, string code)
        {
            // Only known code/status pairs reach the application. Never echo arbitrary server text.
            if (status == 429) return Error("AUTH.RATE_LIMITED", "Session endpoint rate limited", RetryPolicy.Delayed);
            if (status == 403 && code == "AUTH.APP_NOT_ALLOWED") return Error(code, "Game, application or environment is not allowed by server configuration", RetryPolicy.Never);
            if (status == 409 && code == "AUTH.CODE_REUSED") return Error(code, "Temporary credential was already submitted; obtain a new login code", RetryPolicy.UserAction);
            if (status == 409 && code == "AUTH.LOGIN_REVOKED") return Error(code, "Game sessions were revoked during login; explicitly start a new login", RetryPolicy.UserAction);
            if (status == 401 && code == "AUTH.PLATFORM_REJECTED") return Error(code, "Platform rejected the temporary credential", RetryPolicy.UserAction);
            if (status == 400 && code == "AUTH.PLATFORM_UNSUPPORTED") return Error(code, "Server has no verifier for this platform", RetryPolicy.Never);
            if (status == 400 && code == "REQUEST.INVALID") return Error(code, "Session request fields are invalid", RetryPolicy.Never);
            if (status == 413 && code == "REQUEST.TOO_LARGE") return Error(code, "Session request exceeds server size limit", RetryPolicy.Never);
            if (status == 415 && code == "REQUEST.CONTENT_TYPE") return Error(code, "Session request content type is unsupported", RetryPolicy.Never);
            if (status == 502 && code == "AUTH.PLATFORM_UNAVAILABLE") return Error(code, "Server could not reach the platform; retry with a new login code", RetryPolicy.Delayed);
            if (status == 502 && code == "AUTH.PLATFORM_CONFIGURATION") return Error(code, "Platform rejected server application configuration; contact the game operator", RetryPolicy.Never);
            if (status == 502 && code == "AUTH.PLATFORM_RESPONSE_INVALID") return Error(code, "Server received an invalid platform response", RetryPolicy.Unknown);
            if (status == 502 && code == "AUTH.INVALID_IDENTITY") return Error(code, "Server verifier returned no valid identity", RetryPolicy.Never);
            if (status == 503 && code == "SERVER.CAPACITY") return Error(code, "Session service is at capacity", RetryPolicy.Delayed);
            if (status == 503 && code == "AUTH.CREDENTIAL_STORAGE_FAILED") return Error(code, "Server could not save platform credentials; retry later with a new login code", RetryPolicy.Delayed);
            return Error("AUTH.SERVER_REJECTED", "Session endpoint rejected request", RetryPolicy.Unknown);
        }
        static SdkError Error(string code, string reason, RetryPolicy retry) { return new SdkError(code, reason).WithRetry(retry); }
    }
}
