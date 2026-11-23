using System;
using System.Collections.Generic;
using ErDon.UnitySDK;

class AnalyticsFacadeTests
{
    class Provider : IAnalytics, IStorage
    {
        public int Calls, Disposals;
        public Func<AnalyticsResult> Invoke = () => AnalyticsResult.DispatchAccepted();
        public AnalyticsResult ReportEvent(AnalyticsEvent value) { Calls++; return Invoke(); }
        public void Dispose() { Disposals++; }
        public StorageResult Set(string key, string value) { return StorageResult.Ok(); }
        public StorageResult Get(string key) { return StorageResult.Ok("value"); }
        public StorageResult Remove(string key) { return StorageResult.Ok(); }
    }
    class Factory : ISdkProviderFactory, IAnalyticsProviderFactory, IPayProviderFactory
    {
        public Provider Provider = new Provider();
        public int Creates;
        public IAuth CreateAuth(Action<Action> dispatch) { throw new Exception("disabled auth created"); }
        public IStorage CreateStorage() { return Provider; }
        public IAnalytics CreateAnalytics() { Creates++; return Provider; }
        public IPay CreatePay(Action<Action> dispatch) { throw new Exception("creation failure"); }
    }
    static readonly List<string> Logs = new List<string>();
    static void Log(SdkLogLevel level, string text) { Logs.Add(text); }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Reject(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Expected lifecycle rejection"); }
    static void Main()
    {
        var item = new AnalyticsEvent("private_event", new Dictionary<string, object> { { "private_key", "private_value" } });
        Check(SDK.ReportEvent(item).Error.Code == "SDK.NOT_INITIALIZED", "uninitialized");
        var factory = new Factory();
        var config = new SdkConfig { Platform = "Mock", LoginEnabled = false };
        SDK.InitializeWithFactory(config, factory, a => a(), Log);
        Check(factory.Creates == 0 && SDK.ReportEvent(item).Error.Code == "MODULE.DISABLED", "disabled factory");
        config.AnalyticsEnabled = true;
        Check(SDK.ReportEvent(item).Error.Code == "MODULE.DISABLED", "config snapshot");
        SDK.InitializeWithFactory(config, factory, a => a(), Log);
        Check(factory.Creates == 1, "one creation");
        var first = SDK.ReportEvent(item);
        Check(first.Dispatched && !string.IsNullOrEmpty(first.CallId), "dispatch correlation");
        Check(SDK.ReportEvent(null).Error.Code == "ANALYTICS.INVALID_ARGUMENT" && factory.Provider.Calls == 1, "invalid event not dispatched");
        var shared = AnalyticsResult.Fail("ANALYTICS.UNAVAILABLE", "Unavailable");
        factory.Provider.Invoke = () => shared;
        var fail1 = SDK.ReportEvent(item); var fail2 = SDK.ReportEvent(item);
        Check(fail1.CallId != fail2.CallId && fail1.Error.CallId == fail1.CallId && shared.CallId == null && shared.Error.CallId == null, "error snapshots");
        Check(fail1.Error.Operation == "SDK.ReportEvent" && fail1.Error.Stage == "Analytics", "error diagnostics");
        factory.Provider.Invoke = () => null;
        Check(SDK.ReportEvent(item).Error.Code == "ANALYTICS.INVALID_RESULT", "null result");
        factory.Provider.Invoke = () => { SDK.Shutdown(); return AnalyticsResult.DispatchAccepted(); };
        Check(SDK.ReportEvent(item).Error.Code == "ANALYTICS.PROVIDER_FAILED" && factory.Provider.Disposals == 0, "provider cannot destroy active sdk");
        Reject(() => SDK.InitializeWithFactory(config, factory, a => a(), Log));
        Check(factory.Provider.Disposals == 0, "owned instance rollback protection");
        var broken = new Factory();
        var brokenConfig = new SdkConfig { Platform = "Mock", LoginEnabled = false, StorageEnabled = true, AnalyticsEnabled = true, PayEnabled = true };
        try { SDK.InitializeWithFactory(brokenConfig, broken, a => a(), Log); throw new Exception("accepted broken factory"); }
        catch (Exception e) { Check(e.Message == "creation failure", "expected factory exception"); }
        Check(broken.Provider.Disposals == 1 && factory.Provider.Disposals == 0, "multi-capability rollback once, preserve active");
        Check(!string.Join("\n", Logs.ToArray()).Contains("private_"), "event contents leaked");
        Check(string.Join("\n", Logs.ToArray()).Contains(first.CallId), "logs correlate");
        SDK.Shutdown();
        Check(factory.Provider.Disposals == 1, "shutdown ownership");
        var both = new Provider();
        SDK.Initialize(new SdkConfig { LoginEnabled = false, AnalyticsEnabled = true, StorageEnabled = true }, null, a => a(), Log, storageProvider: both, analyticsProvider: both);
        SDK.Shutdown();
        Check(both.Disposals == 1, "shared provider disposed once");
        SDK.Initialize(config, null, a => a(), Log);
        Check(SDK.ReportEvent(item).Error.Code == "CAPABILITY.NOT_SUPPORTED", "missing provider");
        SDK.Shutdown();
        Console.WriteLine("PASS: analytics facade toggles, ownership, rollback, reentry, snapshots and redacted correlated logs");
    }
}
