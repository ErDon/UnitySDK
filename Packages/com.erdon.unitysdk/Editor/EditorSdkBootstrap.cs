using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace ErDon.UnitySDK.Editor
{
    public enum MockLoginOutcome { Success, Cancelled, Denied, Timeout, RateLimited }
    public enum MockPaymentOutcome { Fulfilled, Pending, Cancelled, Timeout }
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
        sealed class MockFactory : ISdkProviderFactory, IShareProviderFactory, IPayProviderFactory
        {
            readonly MockLoginOutcome outcome;
            readonly MockPaymentOutcome payment;
            public MockFactory(MockLoginOutcome outcome, MockPaymentOutcome payment) { this.outcome = outcome; this.payment = payment; }
            public IAuth CreateAuth(Action<Action> post) { return new EditorMockAuth(outcome); }
            public IStorage CreateStorage() { return new EditorMockStorage(); }
            public IShare CreateShare() { return new EditorMockShare(); }
            public IPay CreatePay(Action<Action> post) { return new EditorMockPay(payment); }
        }
        public static void Initialize(MockLoginOutcome outcome, Action<SdkLogLevel, string> logger = null, MockPaymentOutcome payment = MockPaymentOutcome.Fulfilled)
        {
            var settings = SdkProjectSettings.instance;
            var issues = settings.Validate();
            if (issues.Count != 0) throw new InvalidOperationException(string.Join("\n", issues));
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode before initializing the SDK host");
            UnitySdkHost.InitializeWithFactory(new SdkConfig {
                Platform = "Mock", LoginEnabled = settings.login, StorageEnabled = settings.storage, ShareEnabled = settings.share, PayEnabled = settings.payments, LogCallback = logger
            }, new MockFactory(outcome, payment));
        }
    }
    public sealed class EditorMockPay : IPay
    {
        readonly MockPaymentOutcome outcome;
        public EditorMockPay(MockPaymentOutcome outcome) { this.outcome = outcome; }
        public void Pay(PaymentRequest request, Action<PaymentResult> callback)
        {
            if(outcome == MockPaymentOutcome.Timeout)return;
            if(outcome == MockPaymentOutcome.Cancelled){callback(PaymentResult.Fail("PAY.USER_CANCELLED","Mock cancellation"));return;}
            callback(PaymentResult.FromOrder(OrderResult.Ok(new GameOrder(Guid.NewGuid().ToString("D"),request.ProductId,99,"USD",
                outcome==MockPaymentOutcome.Fulfilled?OrderState.Fulfilled:OrderState.Paid))));
        }
        public void Dispose() { }
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
