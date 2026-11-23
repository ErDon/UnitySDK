using System;
using System.Collections.Generic;
using ErDon.UnitySDK;

class AnalyticsContractTests
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Reject(Action action)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new Exception("Invalid event accepted");
    }
    static void Main()
    {
        var input = new Dictionary<string, object> { { "level", 4 }, { "duration", 1.25 }, { "mode", "solo" } };
        var item = new AnalyticsEvent("level_end", input);
        input["level"] = 9;
        item.Parameters.Clear();
        Check((int)item.Parameters["level"] == 4 && item.Parameters.Count == 3, "Caller mutation changed event");
        Check(new AnalyticsEvent("empty").Parameters.Count == 0, "Empty event");
        Reject(() => new AnalyticsEvent(null));
        Reject(() => new AnalyticsEvent("bad\nname"));
        Reject(() => new AnalyticsEvent(new string('a', 65)));
        foreach (var invalid in new object[] { null, true, long.MaxValue, float.NaN, double.NaN, double.PositiveInfinity, new int[] { 1 }, new string('s', 1025) })
            Reject(() => new AnalyticsEvent("event", new Dictionary<string, object> { { "value", invalid } }));
        Reject(() => new AnalyticsEvent("event", new Dictionary<string, object> { { "bad key", 1 } }));
        var many = new Dictionary<string, object>();
        for (int i = 0; i < 33; i++) many.Add("key_" + i, i);
        Reject(() => new AnalyticsEvent("event", many));
        Check(AnalyticsResult.DispatchAccepted().Dispatched, "Dispatch acceptance");
        Check(!AnalyticsResult.Fail("ANALYTICS.UNAVAILABLE", "Unavailable").Dispatched, "Failure must not claim dispatch");
        Reject(() => AnalyticsResult.Fail((SdkError)null));
        Console.WriteLine("PASS: analytics snapshots, portable scalar validation and dispatch-only result semantics");
    }
}
