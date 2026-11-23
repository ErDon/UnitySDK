using System;
using System.Collections.Generic;
using WeChatWASM;

namespace ErDon.UnitySDK.WeChat
{
    /// <summary>Requires an initialized WeChat runtime. Dispatch is not a delivery receipt.</summary>
    public sealed class WeChatAnalytics : IAnalytics
    {
        readonly Action<string, Dictionary<string, object>> report;
        bool disposed;

        public WeChatAnalytics() : this((name, data) => WX.ReportEvent(name, data)) { }

        internal WeChatAnalytics(Action<string, Dictionary<string, object>> report)
        {
            if (report == null) throw new ArgumentNullException("report");
            this.report = report;
        }

        public AnalyticsResult ReportEvent(AnalyticsEvent analyticsEvent)
        {
            if (disposed) return AnalyticsResult.Fail("SDK.DISPOSED", "WeChat analytics was disposed");
            if (analyticsEvent == null) return AnalyticsResult.Fail("ANALYTICS.INVALID_ARGUMENT", "An event is required");
            try
            {
                // Concrete dictionary is serialized by the official generic ReportEvent's LitJson path.
                report(analyticsEvent.Name, new Dictionary<string, object>(analyticsEvent.Parameters, StringComparer.Ordinal));
                return AnalyticsResult.DispatchAccepted();
            }
            catch
            {
                return AnalyticsResult.Fail("ANALYTICS.PROVIDER_FAILED", "WeChat event dispatch failed");
            }
        }

        public void Dispose() { disposed = true; }
    }
}
