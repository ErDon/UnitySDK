using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace ErDon.UnitySDK
{
    public static partial class SDK
    {
        static readonly HashSet<AnalyticsCall> analyticsCalls = new HashSet<AnalyticsCall>();
        sealed class AnalyticsCall : IAnalyticsOperation
        {
            internal readonly string Id = Guid.NewGuid().ToString("N");
            internal readonly int Epoch = generation;
            internal readonly Stopwatch Timer = Stopwatch.StartNew();
            internal int Timeout;
            internal Action<AnalyticsResult> Callback;
            internal Action<Action> Post;
            IAnalyticsOperation operation;
            int completed;
            bool abort;
            internal void Attach(IAnalyticsOperation value)
            {
                operation = value;
                if (abort || Epoch != generation) Abort();
            }
            internal bool Complete(AnalyticsResult result)
            {
                if (Epoch != generation || Interlocked.Exchange(ref completed, 1) != 0) return false;
                var snapshot = result == null ? AnalyticsResult.Fail("ANALYTICS.INVALID_RESULT", "Analytics provider returned no result") : result.Snapshot();
                PostCompletion(Post, Epoch, Id, "SDK.ReportEvent", () => {
                    analyticsCalls.Remove(this);
                    var callback = Callback; Callback = null;
                    FinishAnalytics(callback, snapshot, Id);
                });
                return true;
            }
            void Abort()
            {
                abort = true; var current = operation; operation = null;
                if (current != null) try { current.Cancel(); }
                catch { Log(SdkLogLevel.Warning, "SDK.ReportEvent", Id, "Cancel", "{}", "ANALYTICS.CANCEL_FAILED"); }
            }
            public void Cancel()
            {
                EnsureNotLogging();
                if (Complete(AnalyticsResult.Fail("ANALYTICS.CANCELLED", "Local event observation was cancelled"))) Abort();
            }
            internal void Tick()
            {
                if (Timer.ElapsedMilliseconds >= Timeout && Complete(AnalyticsResult.Fail("ANALYTICS.TIMEOUT", "Local event observation timed out"))) Abort();
            }
            internal void Abandon() { Interlocked.Exchange(ref completed, 1); Callback = null; Abort(); }
        }
        sealed class FinishedAnalyticsCall : IAnalyticsOperation { public void Cancel() { } }

        /// <summary>Observe platform completion where available; synchronous platforms complete after dispatch only.</summary>
        public static IAnalyticsOperation ReportEvent(AnalyticsEvent analyticsEvent, Action<AnalyticsResult> callback, int timeoutMilliseconds = 15000)
        {
            EnsureNotLogging();
            if (callback == null) throw new ArgumentNullException("callback");
            if (timeoutMilliseconds <= 0 || timeoutMilliseconds > 120000) throw new ArgumentOutOfRangeException("timeoutMilliseconds");
            var call = new AnalyticsCall { Callback = callback, Post = dispatch, Timeout = timeoutMilliseconds };
            Log(SdkLogLevel.Info, "SDK.ReportEvent", call.Id, "Start", "{\"content\":\"[REDACTED]\"}", "Pending");
            string invalid = config == null ? "SDK.NOT_INITIALIZED" : !config.AnalyticsEnabled ? "MODULE.DISABLED" : analytics == null ? "CAPABILITY.NOT_SUPPORTED" : analyticsEvent == null ? "ANALYTICS.INVALID_ARGUMENT" : null;
            if (invalid != null) { FinishAnalytics(callback, AnalyticsResult.Fail(invalid, invalid), call.Id); return new FinishedAnalyticsCall(); }
            analyticsCalls.Add(call);
            try
            {
                var asyncProvider = analytics as IAsyncAnalytics;
                if (asyncProvider != null)
                {
                    var operation = asyncProvider.ReportEvent(analyticsEvent, result => call.Complete(result));
                    call.Attach(operation);
                    if (operation == null) call.Complete(AnalyticsResult.Fail("ANALYTICS.INVALID_OPERATION", "Analytics provider returned no cancellation handle"));
                }
                else call.Complete(analytics.ReportEvent(analyticsEvent));
            }
            catch { call.Complete(AnalyticsResult.Fail("ANALYTICS.PROVIDER_FAILED", "Analytics provider invocation failed")); }
            return call;
        }
        static void FinishAnalytics(Action<AnalyticsResult> callback, AnalyticsResult result, string call)
        {
            result = result.Snapshot(); result.CallId = call;
            if (result.Error != null) { result.Error.CallId = call; result.Error.Operation = "SDK.ReportEvent"; result.Error.Stage = "Analytics"; }
            Log(result.Dispatched ? SdkLogLevel.Info : SdkLogLevel.Error, "SDK.ReportEvent", call, "Complete", "{}", result.Dispatched ? "Dispatched" : result.Error.Code);
            try { callback(result); }
            catch { Log(SdkLogLevel.Error, "SDK.ReportEvent", call, "Callback", "{}", "CALLBACK.EXCEPTION"); }
        }
        static void TickAnalytics() { foreach (var call in new List<AnalyticsCall>(analyticsCalls)) call.Tick(); }
        static void ResetAnalyticsCalls()
        {
            var old = new List<AnalyticsCall>(analyticsCalls); analyticsCalls.Clear();
            foreach (var call in old) call.Abandon();
        }

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
