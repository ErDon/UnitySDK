using System;
using System.Diagnostics;
using System.Threading;

namespace ErDon.UnitySDK
{
    public enum SdkLogLevel { Info, Warning, Error }
    public sealed class SdkError
    {
        public string Code { get; private set; }
        public string Reason { get; private set; }
        public SdkError(string code, string reason) { Code = code; Reason = reason; }
    }
    public sealed class LoginResult
    {
        public bool Success { get { return Error == null; } }
        public string Session { get; private set; }
        public SdkError Error { get; private set; }
        public static LoginResult Ok(string session) { return new LoginResult { Session = session }; }
        public static LoginResult Fail(string code, string reason) { return new LoginResult { Error = new SdkError(code, reason) }; }
    }
    public interface IAuth : IDisposable
    {
        void Login(Action<LoginResult> callback);
    }
    public sealed class SdkConfig
    {
        public string Platform = "Unconfigured";
        public bool LoginEnabled = true;
        public Action<SdkLogLevel, string> LogCallback;
    }
    // Internal composition keeps provider selection out of game-facing calls.
    public static class SDK
    {
        static IAuth auth;
        static SdkConfig config;
        static Action<Action> dispatch;
        static Action<SdkLogLevel, string> fallback;
        static int generation;
        static bool busy;
        public static void Initialize(SdkConfig settings, IAuth provider,
            Action<Action> mainThreadDispatch, Action<SdkLogLevel, string> engineLog)
        {
            if (settings == null || mainThreadDispatch == null || engineLog == null) throw new ArgumentNullException();
            Shutdown();
            config = new SdkConfig { Platform = settings.Platform, LoginEnabled = settings.LoginEnabled, LogCallback = settings.LogCallback };
            auth = provider; dispatch = mainThreadDispatch; fallback = engineLog;
            Log(SdkLogLevel.Info, "SDK.Initialize", Guid.NewGuid().ToString("N"), "Complete", "{}", "Success");
        }
        static string Safe(string value) { return (value ?? "").Replace("\r", " ").Replace("\n", " "); }
        static void Log(SdkLogLevel level, string api, string call, string stage, string parameters, string result)
        {
            var sink = config == null ? null : config.LogCallback;
            var backup = fallback;
            string line = DateTimeOffset.Now.ToString("o") + " [" + level + "] platform=" + Safe(config == null ? "Unconfigured" : config.Platform)
                + " interface=" + api + " callId=" + call + " logId=" + Guid.NewGuid().ToString("N")
                + " stage=" + stage + " params=" + parameters + " result=" + Safe(result);
            try { if (sink != null) { sink(level, line); return; } } catch { }
            try { if (backup != null) backup(level, line); } catch { }
        }
        public static void Login(Action<LoginResult> callback)
        {
            if (callback == null) throw new ArgumentNullException("callback");
            if (config == null) { callback(LoginResult.Fail("SDK.NOT_INITIALIZED", "Initialize SDK first")); return; }
            string call = Guid.NewGuid().ToString("N");
            Log(SdkLogLevel.Info, "SDK.Login", call, "Start", "{}", "Pending");
            if (!config.LoginEnabled) { FinishImmediate(callback, call, "MODULE.DISABLED"); return; }
            if (auth == null) { FinishImmediate(callback, call, "CAPABILITY.NOT_SUPPORTED"); return; }
            if (busy) { FinishImmediate(callback, call, "AUTH.IN_PROGRESS"); return; }
            busy = true;
            int epoch = generation, completed = 0;
            var post = dispatch;
            var timer = Stopwatch.StartNew();
            Action<LoginResult> done = result => {
                if (Interlocked.Exchange(ref completed, 1) != 0) return;
                post(() => {
                    if (epoch != generation) return;
                    busy = false;
                    result = result ?? LoginResult.Fail("AUTH.UNKNOWN", "Provider returned no result");
                    // Never log session credentials or arbitrary provider messages.
                    Log(result.Success ? SdkLogLevel.Info : SdkLogLevel.Error, "SDK.Login", call, "Complete", "{}",
                        (result.Success ? "Success" : result.Error.Code) + " durationMs=" + timer.ElapsedMilliseconds);
                    callback(result);
                });
            };
            try { auth.Login(done); } catch { done(LoginResult.Fail("AUTH.UNKNOWN", "Provider threw an exception")); }
        }
        static void FinishImmediate(Action<LoginResult> callback, string call, string code)
        {
            Log(SdkLogLevel.Warning, "SDK.Login", call, "Complete", "{}", code);
            callback(LoginResult.Fail(code, code));
        }
        public static void Shutdown()
        {
            generation++; busy = false;
            var old = auth; auth = null; config = null;
            if (old != null) { try { old.Dispose(); } catch { } }
        }
    }
}
