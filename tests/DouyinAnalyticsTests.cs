using System;
using System.Collections.Generic;
using ErDon.UnitySDK;
using ErDon.UnitySDK.Douyin;

class DouyinAnalyticsTests
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Main()
    {
        int calls = 0;
        Dictionary<string, object> payload = null;
        var item = new AnalyticsEvent("level_end", new Dictionary<string, object> { { "level", 3 }, { "duration", 2.5 }, { "mode", "solo" } });
        var provider = new DouyinAnalytics((name, data) => {
            calls++; Check(name == "level_end", "event name mapping"); payload = data;
        });
        Check(provider.ReportEvent(item).Dispatched && calls == 1, "single dispatch");
        Check((int)payload["level"] == 3 && (double)payload["duration"] == 2.5 && (string)payload["mode"] == "solo", "scalar mapping");
        // Match the scalar JsonData construction in TTAPIImpl.ReportAnalytics<T>.
        var serialized = new TTSDK.UNBridgeLib.LitJson.JsonData();
        foreach (var pair in payload) serialized[pair.Key] = new TTSDK.UNBridgeLib.LitJson.JsonData(pair.Value);
        var json = serialized.ToJson();
        Check(json.Contains("\"level\":3") && json.Contains("\"duration\":2.5") && json.Contains("\"mode\":\"solo\""), "official JSON serializer mapping");
        payload.Clear();
        Check(item.Parameters.Count == 3, "platform mutation must not change source event");
        Check(!provider.ReportEvent(null).Dispatched && calls == 1, "null event not sent");
        var reserved = new AnalyticsEvent("event", new Dictionary<string, object> { { "cp_event_key_name", "override" } });
        Check(provider.ReportEvent(reserved).Error.Code == "ANALYTICS.RESERVED_PARAMETER" && calls == 1, "prevent bridge event-name overwrite");
        provider.Dispose(); provider.Dispose();
        Check(provider.ReportEvent(item).Error.Code == "SDK.DISPOSED" && calls == 1, "disposed calls suppressed");
        var failing = new DouyinAnalytics((name, data) => { throw new Exception("secret payload"); });
        var error = failing.ReportEvent(item).Error;
        Check(error.Code == "ANALYTICS.PROVIDER_FAILED" && !error.Reason.Contains("secret"), "safe failure");
        var factory = new DouyinProviderFactory("game", "test");
        var first = factory.CreateAnalytics(); var second = factory.CreateAnalytics();
        Check(!object.ReferenceEquals(first, second), "fresh factory instances"); first.Dispose(); second.Dispose();
        SDK.InitializeWithFactory(new SdkConfig { Platform = "Douyin", LoginEnabled = false, AnalyticsEnabled = true }, factory, action => action(), (level, text) => {});
        SDK.Shutdown(); // Creation and release make no platform calls.
        Console.WriteLine("DOUYIN_ANALYTICS_TESTS_PASS (official compile/serializer and injected dispatch; not device receipt)");
    }
}
