using System;
using System.Collections.Generic;
using System.Threading;
using ErDon.UnitySDK;

class AnalyticsAsyncTests
{
    class Operation : IAnalyticsOperation { public int Cancels; public void Cancel() { Cancels++; } }
    class Provider : IAnalytics, IAsyncAnalytics
    {
        public List<Action<AnalyticsResult>> Callbacks = new List<Action<AnalyticsResult>>();
        public List<Operation> Operations = new List<Operation>();
        public int Disposals;
        public AnalyticsResult ReportEvent(AnalyticsEvent value) { throw new Exception("Async path must be selected"); }
        public IAnalyticsOperation ReportEvent(AnalyticsEvent value, Action<AnalyticsResult> callback)
        { Callbacks.Add(callback); var operation = new Operation(); Operations.Add(operation); return operation; }
        public void Dispose() { Disposals++; }
    }
    class SyncProvider : IAnalytics
    {
        public AnalyticsResult ReportEvent(AnalyticsEvent value) { return AnalyticsResult.DispatchAccepted(); }
        public void Dispose() { }
    }
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Main()
    {
        var queue = new Queue<Action>(); var logs = new List<string>();
        var provider = new Provider();
        var config = new SdkConfig { LoginEnabled = false, AnalyticsEnabled = true };
        SDK.Initialize(config, null, action => queue.Enqueue(action), (level, text) => logs.Add(text), analyticsProvider: provider);
        var item = new AnalyticsEvent("secret_name");
        int count = 0; AnalyticsResult first = null, second = null;
        var handle = SDK.ReportEvent(item, value => { count++; first = value; });
        SDK.ReportEvent(item, value => { count++; second = value; });
        Check(provider.Operations.Count == 2 && count == 0, "independent concurrent calls");
        var shared = AnalyticsResult.Fail("ANALYTICS.PLATFORM_FAILED", "Failed");
        provider.Callbacks[1](shared); provider.Callbacks[1](AnalyticsResult.DispatchAccepted());
        handle.Cancel(); handle.Cancel(); provider.Callbacks[0](AnalyticsResult.DispatchAccepted());
        Check(count == 0, "delivery must use dispatcher");
        while (queue.Count > 0) queue.Dequeue()();
        Check(count == 2 && first.Error.Code == "ANALYTICS.CANCELLED" && second.Error.Code == "ANALYTICS.PLATFORM_FAILED", "first outcome per call");
        Check(first.CallId != second.CallId && second.Error.CallId == second.CallId && shared.CallId == null, "correlation and snapshot");
        Check(provider.Operations[0].Cancels == 1, "cancel underlying once");
        SDK.ReportEvent(item, value => { count++; first = value; }, 1);
        Thread.Sleep(10); SDK.Tick(); while (queue.Count > 0) queue.Dequeue()();
        Check(count == 3 && first.Error.Code == "ANALYTICS.TIMEOUT" && provider.Operations[2].Cancels == 1, "timeout aborts local observation");
        SDK.ReportEvent(item, value => count++);
        provider.Callbacks[3](AnalyticsResult.DispatchAccepted());
        SDK.Shutdown(); while (queue.Count > 0) queue.Dequeue()();
        Check(count == 3 && provider.Disposals == 1, "queued result suppressed after shutdown");
        Check(!string.Join("\n", logs.ToArray()).Contains("secret_name"), "event privacy");
        SDK.Initialize(config, null, action => action(), (level, text) => {}, analyticsProvider: new SyncProvider());
        SDK.ReportEvent(item, value => { Check(value.Dispatched, "sync platform dispatch"); count++; });
        Check(count == 4, "callback facade supports sync platforms");
        SDK.Shutdown();
        // A broken dispatcher must be recovered by Tick, not lose the completion.
        provider = new Provider();
        SDK.Initialize(config, null, action => { throw new Exception("dispatcher unavailable"); }, (level, text) => {}, analyticsProvider: provider);
        SDK.ReportEvent(item, value => count++);
        provider.Callbacks[0](AnalyticsResult.DispatchAccepted());
        Check(count == 4, "no inline fallback"); SDK.Tick(); Check(count == 5, "dispatch recovery");
        SDK.Shutdown();
        Console.WriteLine("PASS: async analytics concurrency, cancellation, timeout, snapshots, dispatch recovery and shutdown");
    }
}
