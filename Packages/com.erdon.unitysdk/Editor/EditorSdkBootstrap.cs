using System;
using System.Collections.Generic;
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
        sealed class MockFactory : ISdkProviderFactory, IShareProviderFactory
        {
            readonly MockLoginOutcome outcome;
            public MockFactory(MockLoginOutcome outcome) { this.outcome = outcome; }
            public IAuth CreateAuth(Action<Action> post) { return new EditorMockAuth(outcome); }
            public IStorage CreateStorage() { return new EditorMockStorage(); }
            public IShare CreateShare() { return new EditorMockShare(); }
        }
        public static void Initialize(MockLoginOutcome outcome, Action<SdkLogLevel, string> logger = null)
        {
            var settings = SdkProjectSettings.instance;
            var issues = settings.Validate();
            if (issues.Count != 0) throw new InvalidOperationException(string.Join("\n", issues));
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode before initializing the SDK host");
            UnitySdkHost.InitializeWithFactory(new SdkConfig {
                Platform = "Mock", LoginEnabled = settings.login, StorageEnabled = settings.storage, ShareEnabled = settings.share, LogCallback = logger
            }, new MockFactory(outcome));
        }
    }
    public sealed class EditorMockShare : IShare
    {
        public void Share(ShareRequest request, Action<ShareResult> callback) { callback(ShareResult.Dispatched()); }
        public void Dispose() { }
    }
    public sealed class EditorMockStorage : IStorage
    {
        readonly Dictionary<string, string> data = new Dictionary<string, string>();
        public StorageResult Set(string key, string value) { data[key] = value; return StorageResult.Ok(); }
        public StorageResult Get(string key) {
            string value;
            return data.TryGetValue(key, out value) ? StorageResult.Ok(value) : StorageResult.Fail("STORAGE.NOT_FOUND", "Storage key does not exist");
        }
        public StorageResult Remove(string key) { data.Remove(key); return StorageResult.Ok(); }
        public void Dispose() { data.Clear(); }
    }
}
