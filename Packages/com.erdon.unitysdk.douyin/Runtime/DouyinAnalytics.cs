#if !ERDON_EXCLUDE_ANALYTICS || UNITY_EDITOR
using System;
using System.Collections.Generic;
using TTSDK;

namespace ErDon.UnitySDK.Douyin
{
    /// <summary>Requires an initialized Douyin runtime. Dispatch is not a delivery receipt.</summary>
    public sealed class DouyinAnalytics : IAnalytics
    {
        readonly Action<string, Dictionary<string, object>> report;
        bool disposed;

        public DouyinAnalytics() : this((name, data) => TT.ReportAnalytics(name, data)) { }

        internal DouyinAnalytics(Action<string, Dictionary<string, object>> report)
        {
            if (report == null) throw new ArgumentNullException("report");
            this.report = report;
        }

        public AnalyticsResult ReportEvent(AnalyticsEvent analyticsEvent)
        {
            if (disposed) return AnalyticsResult.Fail("SDK.DISPOSED", "Douyin analytics was disposed");
            if (analyticsEvent == null) return AnalyticsResult.Fail("ANALYTICS.INVALID_ARGUMENT", "An event is required");
            if (analyticsEvent.Parameters.ContainsKey("cp_event_key_name"))
                return AnalyticsResult.Fail("ANALYTICS.RESERVED_PARAMETER", "Douyin reserves cp_event_key_name for the event name");
            try
            {
                // The official bridge builds JsonData from these scalar values.
                report(analyticsEvent.Name, new Dictionary<string, object>(analyticsEvent.Parameters, StringComparer.Ordinal));
                return AnalyticsResult.DispatchAccepted();
            }
            catch
            {
                return AnalyticsResult.Fail("ANALYTICS.PROVIDER_FAILED", "Douyin event dispatch failed");
            }
        }

        public void Dispose() { disposed = true; }
    }
}

#endif
