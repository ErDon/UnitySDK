using System;
using System.Collections.Generic;

namespace ErDon.UnitySDK
{
    /// <summary>A flat event snapshot. Limits are SDK policy, not platform quotas.</summary>
    public sealed class AnalyticsEvent
    {
        private readonly Dictionary<string, object> parameters;
        public string Name { get; private set; }
        public IDictionary<string, object> Parameters
        {
            get { return new Dictionary<string, object>(parameters, StringComparer.Ordinal); }
        }

        public AnalyticsEvent(string name, IDictionary<string, object> parameters = null)
        {
            ValidateName(name, "name");
            Name = name;
            this.parameters = new Dictionary<string, object>(StringComparer.Ordinal);
            if (parameters == null) return;
            if (parameters.Count > 32) throw new ArgumentException("At most 32 event parameters are supported.", "parameters");
            foreach (var item in parameters)
            {
                ValidateName(item.Key, "parameters");
                var value = item.Value;
                // Avoid serializer-dependent objects, mutable collections and integer precision loss.
                if (value is string)
                {
                    if (((string)value).Length > 1024) throw new ArgumentException("Event string exceeds 1024 characters.", "parameters");
                }
                else if (value is double)
                {
                    var number = (double)value;
                    if (double.IsNaN(number) || double.IsInfinity(number)) throw new ArgumentException("Event numbers must be finite.", "parameters");
                }
                else if (!(value is int))
                    throw new ArgumentException("Event values must be string, Int32 or finite Double.", "parameters");
                this.parameters.Add(item.Key, value);
            }
        }

        private static void ValidateName(string name, string argument)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 64)
                throw new ArgumentException("Event and parameter names must contain 1 to 64 ASCII letters, digits or underscores.", argument);
            foreach (var character in name)
                if (!((character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z') ||
                      (character >= '0' && character <= '9') || character == '_'))
                    throw new ArgumentException("Event and parameter names must contain only ASCII letters, digits or underscores.", argument);
        }
    }

    public sealed class AnalyticsResult
    {
        public string CallId { get; internal set; }
        public string TraceId { get { return CallId; } }
        /// <summary>The platform invocation returned normally. This is NOT a delivery receipt.</summary>
        public bool Dispatched { get { return Error == null; } }
        public SdkError Error { get; private set; }
        public static AnalyticsResult DispatchAccepted() { return new AnalyticsResult(); }
        public static AnalyticsResult Fail(SdkError error)
        {
            if (error == null) throw new ArgumentNullException("error");
            return new AnalyticsResult { Error = error };
        }
        public static AnalyticsResult Fail(string code, string reason) { return Fail(new SdkError(code, reason)); }
        internal AnalyticsResult Snapshot()
        {
            var copy = (AnalyticsResult)MemberwiseClone();
            copy.Error = Error == null ? null : Error.Snapshot();
            return copy;
        }
    }

    public interface IAnalytics : IDisposable
    {
        AnalyticsResult ReportEvent(AnalyticsEvent analyticsEvent);
    }

    public interface IAnalyticsProviderFactory
    {
        IAnalytics CreateAnalytics();
    }
}
