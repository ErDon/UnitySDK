using System;
using System.Collections.Generic;
using TTSDK;
using TTSDK.UNBridgeLib.LitJson;

namespace ErDon.UnitySDK.TikTok
{
    /// <summary>Completion-aware adapter; initialize the official runtime before reporting.</summary>
    public sealed class TikTokAnalytics : IAnalytics, IAsyncAnalytics
    {
        readonly Action<ReportEventParam> report;
        readonly object gate = new object();
        readonly HashSet<Operation> pending = new HashSet<Operation>();
        bool disposed;

        public TikTokAnalytics() : this(TT.ReportEvent) { }
        internal TikTokAnalytics(Action<ReportEventParam> report)
        {
            if (report == null) throw new ArgumentNullException("report");
            this.report = report;
        }

        public AnalyticsResult ReportEvent(AnalyticsEvent analyticsEvent)
        {
            return AnalyticsResult.Fail("ANALYTICS.ASYNC_REQUIRED", "TikTok analytics requires the callback overload of SDK.ReportEvent");
        }

        public IAnalyticsOperation ReportEvent(AnalyticsEvent analyticsEvent, Action<AnalyticsResult> callback)
        {
            if (callback == null) throw new ArgumentNullException("callback");
            var operation = new Operation(this, callback);
            bool unavailable;
            lock (gate) { unavailable = disposed; if (!unavailable) pending.Add(operation); }
            if (unavailable) { operation.Finish(AnalyticsResult.Fail("SDK.DISPOSED", "TikTok analytics was disposed")); return operation; }
            if (analyticsEvent == null) { operation.Finish(AnalyticsResult.Fail("ANALYTICS.INVALID_ARGUMENT", "An event is required")); return operation; }
            try
            {
                var data = new JsonData();
                // An empty event must serialize as an object, not null or the string "{}".
                data.SetJsonType(JsonType.Object);
                foreach (var item in analyticsEvent.Parameters) data[item.Key] = new JsonData(item.Value);
                report(new ReportEventParam {
                    eventName = analyticsEvent.Name, @params = data,
                    success = () => operation.Finish(AnalyticsResult.DispatchAccepted()),
                    fail = () => operation.Finish(AnalyticsResult.Fail("ANALYTICS.PLATFORM_FAILED", "TikTok rejected the event or its bridge timed out")),
                    complete = () => operation.Finish(AnalyticsResult.Fail("ANALYTICS.INVALID_RESULT", "TikTok completed without a success or failure signal"))
                });
            }
            catch { operation.Finish(AnalyticsResult.Fail("ANALYTICS.PROVIDER_FAILED", "TikTok event dispatch failed")); }
            return operation;
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                foreach (var operation in pending) operation.Callback = null;
                pending.Clear();
            }
        }

        sealed class Operation : IAnalyticsOperation
        {
            readonly TikTokAnalytics owner;
            internal Action<AnalyticsResult> Callback;
            internal Operation(TikTokAnalytics owner, Action<AnalyticsResult> callback) { this.owner = owner; Callback = callback; }
            internal void Finish(AnalyticsResult result)
            {
                Action<AnalyticsResult> callback;
                lock (owner.gate) { callback = Callback; Callback = null; owner.pending.Remove(this); }
                if (callback != null) callback(result);
            }
            public void Cancel() { Finish(AnalyticsResult.Fail("ANALYTICS.CANCELLED", "Local event observation was cancelled")); }
        }
    }
}
