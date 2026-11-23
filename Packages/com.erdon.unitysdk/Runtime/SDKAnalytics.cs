using System;

namespace ErDon.UnitySDK
{
    public static partial class SDK
    {
        /// <summary>Dispatch an event. A normal return is not proof of server receipt.</summary>
        public static AnalyticsResult ReportEvent(AnalyticsEvent analyticsEvent)
        {
            EnsureNotLogging();
            const string api = "SDK.ReportEvent";
            string call = Guid.NewGuid().ToString("N");
            // Event names and values can contain business or personal data; only log shape.
            string parameters = "{\"parameterCount\":" + (analyticsEvent == null ? 0 : analyticsEvent.Parameters.Count) + ",\"content\":\"[REDACTED]\"}";
            Log(SdkLogLevel.Info, api, call, "Start", parameters, "Pending");
            AnalyticsResult result;
            if (config == null) result = AnalyticsResult.Fail("SDK.NOT_INITIALIZED", "Initialize SDK before reporting events");
            else if (!config.AnalyticsEnabled) result = AnalyticsResult.Fail("MODULE.DISABLED", "Analytics module is disabled");
            else if (analytics == null) result = AnalyticsResult.Fail("CAPABILITY.NOT_SUPPORTED", "No analytics provider is installed");
            else if (analyticsEvent == null) result = AnalyticsResult.Fail("ANALYTICS.INVALID_ARGUMENT", "An event is required");
            else
            {
                callingAnalytics = true;
                try
                {
                    result = analytics.ReportEvent(analyticsEvent);
                    if (result == null) result = AnalyticsResult.Fail("ANALYTICS.INVALID_RESULT", "Analytics provider returned no result");
                }
                catch { result = AnalyticsResult.Fail("ANALYTICS.PROVIDER_FAILED", "Analytics provider failed"); }
                finally { callingAnalytics = false; }
            }
            result = result.Snapshot();
            result.CallId = call;
            if (result.Error != null)
            {
                result.Error.CallId = call;
                result.Error.Operation = api;
                result.Error.Stage = "Analytics";
            }
            Log(result.Dispatched ? SdkLogLevel.Info : SdkLogLevel.Error, api, call, "Complete", parameters,
                result.Dispatched ? "Dispatched" : result.Error.Code);
            return result;
        }
    }
}
