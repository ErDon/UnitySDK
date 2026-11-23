using System;
using System.Collections.Generic;
namespace ErDon.UnitySDK.Editor
{
    public enum MockAnalyticsOutcome { Dispatched, Failed, Timeout }
    public sealed class EditorMockAnalytics : IAnalytics, IAsyncAnalytics
    {
        readonly MockAnalyticsOutcome outcome;
        readonly HashSet<Operation> pending = new HashSet<Operation>();
        bool disposed;
        public EditorMockAnalytics(MockAnalyticsOutcome outcome) { this.outcome = outcome; }
        public AnalyticsResult ReportEvent(AnalyticsEvent value)
        {
            if (disposed) return AnalyticsResult.Fail("SDK.DISPOSED", "Mock analytics disposed");
            if (value == null) return AnalyticsResult.Fail("ANALYTICS.INVALID_ARGUMENT", "An event is required");
            if (outcome == MockAnalyticsOutcome.Timeout) return AnalyticsResult.Fail("ANALYTICS.ASYNC_REQUIRED", "Timeout simulation requires callback overload");
            return outcome == MockAnalyticsOutcome.Failed ? AnalyticsResult.Fail("ANALYTICS.PLATFORM_FAILED", "Mock platform failure") : AnalyticsResult.DispatchAccepted();
        }
        public IAnalyticsOperation ReportEvent(AnalyticsEvent value, Action<AnalyticsResult> callback)
        {
            if (callback == null) throw new ArgumentNullException("callback");
            var operation = new Operation { Owner = this, Callback = callback };
            if (disposed || value == null) { operation.Complete(ReportEvent(value)); return operation; }
            pending.Add(operation);
            if (outcome != MockAnalyticsOutcome.Timeout) operation.Complete(ReportEvent(value));
            return operation;
        }
        public void Dispose() { disposed = true; foreach (var item in pending) item.Callback = null; pending.Clear(); }
        sealed class Operation : IAnalyticsOperation
        {
            internal EditorMockAnalytics Owner;
            internal Action<AnalyticsResult> Callback;
            internal void Complete(AnalyticsResult result) { var cb = Callback; Callback = null; Owner.pending.Remove(this); if (cb != null) cb(result); }
            public void Cancel() { Complete(AnalyticsResult.Fail("ANALYTICS.CANCELLED", "Mock observation cancelled")); }
        }
    }
}
