#if !ERDON_EXCLUDE_ANALYTICS || UNITY_EDITOR
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

#if ERDON_WECHAT_STRING_ANALYTICS
        public WeChatAnalytics() : this((name, data) => {
            var strings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in data) strings.Add(pair.Key, (string)pair.Value);
            WX.ReportEvent(name, strings);
        }) { }
#else
        public WeChatAnalytics() : this((name, data) => WX.ReportEvent(name, data)) { }
#endif

        internal WeChatAnalytics(Action<string, Dictionary<string, object>> report)
        {
            if (report == null) throw new ArgumentNullException("report");
            this.report = report;
        }

        public AnalyticsResult ReportEvent(AnalyticsEvent analyticsEvent)
        {
            if (disposed) return AnalyticsResult.Fail("SDK.DISPOSED", "WeChat analytics was disposed");
            if (analyticsEvent == null) return AnalyticsResult.Fail("ANALYTICS.INVALID_ARGUMENT", "An event is required");
#if ERDON_WECHAT_STRING_ANALYTICS
            foreach (var pair in analyticsEvent.Parameters)
                if (!(pair.Value is string))
                    return AnalyticsResult.Fail("ANALYTICS.PARAMETER_TYPE_UNSUPPORTED",
                        "This WeChat SDK accepts only string event parameters; numeric values are not converted");
#endif
            try
            {
                // Snapshot prevents a platform callback from mutating the public event.
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

#endif
