using System;
using System.Reflection;
using ErDon.UnitySDK;

class OperationLifecycleTests
{
    sealed class Operation : INetworkOperation, IAnalyticsOperation
    {
        public Action OnCancel;
        public void Cancel() { OnCancel(); }
    }
    sealed class Provider : INetwork, IAnalytics, IAsyncAnalytics
    {
        public bool Disposed, Immediate;
        public int Cancels;
        public Action<NetworkResult> NetworkDone;
        public Action<AnalyticsResult> AnalyticsDone;
        Operation NewOperation() { return new Operation { OnCancel = () => { Check(!Disposed, "cancel after provider dispose"); Cancels++; } }; }
        public INetworkOperation Send(NetworkRequest value, Action<NetworkResult> callback)
        { NetworkDone = callback; if (Immediate) callback(NetworkResult.Response(200, null, null)); return NewOperation(); }
        public AnalyticsResult ReportEvent(AnalyticsEvent value) { return AnalyticsResult.DispatchAccepted(); }
        public IAnalyticsOperation ReportEvent(AnalyticsEvent value, Action<AnalyticsResult> callback)
        { AnalyticsDone = callback; if (Immediate) callback(AnalyticsResult.DispatchAccepted()); return NewOperation(); }
        public void Dispose() { Disposed = true; }
    }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Released(object handle)
    {
        foreach (var name in new[] { "operation", "Callback", "Post" })
        {
            var field = handle.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Check(field != null && field.GetValue(handle) == null, "terminal handle retained " + name);
        }
    }
    static void Initialize(Provider provider)
    {
        SDK.Initialize(new SdkConfig { LoginEnabled = false, NetworkEnabled = true, AnalyticsEnabled = true },
            null, action => action(), (level, text) => {}, networkProvider: provider, analyticsProvider: provider);
    }
    static void Main()
    {
        var request = new NetworkRequest("https://example.com"); var item = new AnalyticsEvent("event");
        var provider = new Provider(); Initialize(provider);
        bool ordered = false;
        var network = SDK.Request(request, value => { ordered = provider.Cancels == 1; SDK.Shutdown(); });
        network.Cancel(); Check(ordered && provider.Disposed && provider.Cancels == 1, "network cancels before user shutdown"); Released(network);
        provider = new Provider(); Initialize(provider); ordered = false;
        var analytics = SDK.ReportEvent(item, value => { ordered = provider.Cancels == 1; SDK.Shutdown(); });
        analytics.Cancel(); Check(ordered && provider.Disposed && provider.Cancels == 1, "analytics cancels before user shutdown"); Released(analytics);
        provider = new Provider(); Initialize(provider);
        network = SDK.Request(request, value => {}); analytics = SDK.ReportEvent(item, value => {});
        provider.NetworkDone(NetworkResult.Response(200, null, null)); provider.AnalyticsDone(AnalyticsResult.DispatchAccepted());
        Released(network); Released(analytics); network.Cancel(); analytics.Cancel(); Check(provider.Cancels == 0, "completed handles do not abort"); SDK.Shutdown();
        // Completion can run before the provider returns its operation handle.
        provider = new Provider { Immediate = true }; Initialize(provider);
        network = SDK.Request(request, value => {}); analytics = SDK.ReportEvent(item, value => {});
        Released(network); Released(analytics); SDK.Shutdown();
        provider = new Provider(); Initialize(provider);
        network = SDK.Request(request, value => { throw new Exception("must suppress stale callback"); });
        analytics = SDK.ReportEvent(item, value => { throw new Exception("must suppress stale callback"); });
        SDK.Shutdown(); Check(provider.Cancels == 2, "shutdown aborts each pending operation"); Released(network); Released(analytics);
        Console.WriteLine("PASS: terminal operation references released and cancellation precedes callback lifecycle changes");
    }
}
