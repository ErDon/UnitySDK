using System;
using UnityEditor;
using UnityEngine;
namespace ErDon.UnitySDK.Editor
{
    public enum MockLoginOutcome { Success, Cancelled, Denied, Timeout, RateLimited }
    // Editor assembly is never compiled into Player builds.
    public sealed class EditorMockAuth : IAuth
    {
        readonly MockLoginOutcome outcome;
        public EditorMockAuth(MockLoginOutcome outcome) { this.outcome = outcome; }
        public void Login(Action<LoginResult> callback)
        {
            switch (outcome) {
                case MockLoginOutcome.Success: callback(LoginResult.Ok("mock-session-not-real")); break;
                case MockLoginOutcome.Cancelled: callback(LoginResult.Fail("AUTH.USER_CANCELLED", "Mock user cancellation")); break;
                case MockLoginOutcome.Denied: callback(LoginResult.Fail("AUTH.PERMISSION_DENIED", "Mock permission denial")); break;
                case MockLoginOutcome.RateLimited: callback(LoginResult.Fail("AUTH.RATE_LIMITED", "Mock rate limit")); break;
                case MockLoginOutcome.Timeout: break;
            }
        }
        public void Dispose() { }
    }
    public static class EditorSdkBootstrap
    {
        public static void Initialize(MockLoginOutcome outcome, Action<SdkLogLevel, string> logger = null)
        {
            var settings = SdkProjectSettings.instance;
            var issues = settings.Validate();
            if (issues.Count != 0) throw new InvalidOperationException(string.Join("\n", issues));
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode before initializing the SDK host");
            UnitySdkHost.Initialize(new SdkConfig {
                Platform = "Mock", LoginEnabled = settings.login, LogCallback = logger
            }, settings.login ? new EditorMockAuth(outcome) : null);
        }
    }
}
