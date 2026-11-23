using System;
using System.Collections.Generic;
using ErDon.UnitySDK;
using ErDon.UnitySDK.TikTok;
using TTSDK;

class TikTokAnalyticsTests
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Main()
    {
        var sent = new List<ReportEventParam>();
        var provider = new TikTokAnalytics(value => sent.Add(value));
        var item = new AnalyticsEvent("level_end", new Dictionary<string, object> { { "level", 2 }, { "duration", 1.25 }, { "mode", "solo" } });
        int callbacks = 0; AnalyticsResult result = null;
        provider.ReportEvent(item, value => { callbacks++; result = value; });
        Check(callbacks == 0, "must wait for platform outcome");
        Check(sent[0].eventName == "level_end" && sent[0].@params.ToJson().Contains("\"duration\":1.25"), "field mapping and numeric JSON");
        sent[0].success(); sent[0].complete(); sent[0].fail();
        Check(callbacks == 1 && result.Dispatched, "first signal wins");
        provider.ReportEvent(new AnalyticsEvent("empty"), value => { callbacks++; result = value; });
        Check(sent[1].@params.ToJson() == "{}", "empty params must be object");
        sent[1].fail(); sent[1].complete();
        Check(callbacks == 2 && result.Error.Code == "ANALYTICS.PLATFORM_FAILED", "failure retained");
        var handle = provider.ReportEvent(item, value => { callbacks++; result = value; });
        handle.Cancel(); handle.Cancel(); sent[2].success();
        Check(callbacks == 3 && result.Error.Code == "ANALYTICS.CANCELLED", "cancel local observation once");
        provider.ReportEvent(item, value => { callbacks++; result = value; });
        sent[3].complete();
        Check(callbacks == 4 && result.Error.Code == "ANALYTICS.INVALID_RESULT", "complete alone is not success");
        provider.ReportEvent(item, value => callbacks++);
        provider.ReportEvent(item, value => callbacks++);
        provider.Dispose(); provider.Dispose(); sent[4].success(); sent[5].fail();
        Check(callbacks == 4, "dispose suppresses pending callbacks");
        provider.ReportEvent(item, value => result = value);
        Check(result.Error.Code == "SDK.DISPOSED" && sent.Count == 6, "disposed calls never dispatch");
        var throwing = new TikTokAnalytics(value => { throw new Exception("secret"); });
        throwing.ReportEvent(item, value => result = value);
        Check(result.Error.Code == "ANALYTICS.PROVIDER_FAILED" && !result.Error.Reason.Contains("secret"), "safe invocation failure");
        var immediate = new TikTokAnalytics(value => { value.success(); throw new Exception("late"); });
        immediate.ReportEvent(item, value => { callbacks++; result = value; });
        Check(callbacks == 5 && result.Dispatched, "success survives later invocation exception");
        immediate.ReportEvent(null, value => result = value);
        Check(result.Error.Code == "ANALYTICS.INVALID_ARGUMENT", "null rejected");
        Console.WriteLine("TIKTOK_ANALYTICS_TESTS_PASS (binary signatures, JSON and injected callbacks; not device ingestion)");
    }
}
