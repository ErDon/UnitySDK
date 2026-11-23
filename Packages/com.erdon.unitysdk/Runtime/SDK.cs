using System;
using System.Diagnostics;
using System.Threading;

namespace ErDon.UnitySDK
{
    public enum RetryPolicy { Unknown, Never, UserAction, Delayed }
    public enum SdkLogLevel { Info, Warning, Error }
    public sealed class SdkError
    {
        public string Code { get; private set; }
        public string Reason { get; private set; }
        public RetryPolicy Retry { get; private set; }
        public int? RetryAfterSeconds { get; private set; }
        public string TraceId { get { return CallId; } }
        public SdkError WithRetry(RetryPolicy policy, int? retryAfterSeconds = null)
        {
            if (retryAfterSeconds.HasValue && retryAfterSeconds.Value < 0) throw new ArgumentOutOfRangeException("retryAfterSeconds");
            Retry = policy; RetryAfterSeconds = retryAfterSeconds; return this;
        }
        public string Operation { get; internal set; }
        public string Stage { get; internal set; }
        public string CallId { get; internal set; }
        public string ServerCallId { get; internal set; }
        public string PlatformCode { get; private set; }
        public string PlatformMessage { get; private set; }
        public SdkError(string code, string reason, string platformCode, string sanitizedPlatformMessage)
            : this(code, reason) { PlatformCode = platformCode; PlatformMessage = sanitizedPlatformMessage; }
        public SdkError(string code, string reason) { Code = code; Reason = reason; }
    }
    public sealed class LoginResult
    {
        public bool Success { get { return Error == null; } }
        public string Session { get; private set; }
        public SdkError Error { get; private set; }
        public string CallId { get; internal set; }
        public string ServerCallId { get; private set; }
        public LoginResult WithServerCallId(string value)
        {
            Guid parsed;
            ServerCallId = Guid.TryParseExact(value, "D", out parsed) ? parsed.ToString("D") : null;
            if (Error != null) Error.ServerCallId = ServerCallId;
            return this;
        }
        public string TraceId { get { return CallId; } }
        public static LoginResult Fail(SdkError error) { if (error == null) throw new ArgumentNullException("error"); return new LoginResult { Error = error }; }
        public static LoginResult Ok(string session) { return new LoginResult { Session = session }; }
        public static LoginResult Fail(string code, string reason) { return new LoginResult { Error = new SdkError(code, reason) }; }
    }
    public interface IAuth : IDisposable
    {
        void Login(Action<LoginResult> callback);
    }
    public sealed class StorageResult
    {
        public bool Success { get { return Error == null; } }
        public string Value { get; private set; }
        public SdkError Error { get; private set; }
        public string CallId { get; internal set; }
        public static StorageResult Ok(string value = null) { return new StorageResult { Value = value }; }
        public static StorageResult Fail(string code, string reason) { return new StorageResult { Error = new SdkError(code, reason) }; }
    }
    public interface IStorage : IDisposable
    {
        StorageResult Set(string key, string value);
        StorageResult Get(string key);
        StorageResult Remove(string key);
    }
    public sealed class SdkConfig
    {
        public string Platform = "Unconfigured";
        public bool LoginEnabled = true;
        public bool StorageEnabled;
        public Action<SdkLogLevel, string> LogCallback;
    }
    // Internal composition keeps provider selection out of game-facing calls.
    public static class SDK
    {
        static IAuth auth;
        static IStorage storage;
        static SdkConfig config;
        static Action<Action> dispatch;
        static Action<SdkLogLevel, string> fallback;
        static int generation;
        static bool busy;
        static Action cancelLogin;
        static Action checkTimeout;
        static bool changingLifecycle;
        static bool callingStorage;
        [ThreadStatic] static bool writingLog;
        public static void Initialize(SdkConfig settings, IAuth provider,
            Action<Action> mainThreadDispatch, Action<SdkLogLevel, string> engineLog, IStorage storageProvider = null)
        {
            EnsureNotLogging();
            if (settings == null || mainThreadDispatch == null || engineLog == null) throw new ArgumentNullException();
            if ((provider != null && (ReferenceEquals(provider, auth) || ReferenceEquals(provider, storage))) ||
                (storageProvider != null && (ReferenceEquals(storageProvider, storage) || ReferenceEquals(storageProvider, auth))))
                throw new ArgumentException("Reinitialization requires a new provider; the SDK owns the previous provider");
            changingLifecycle = true;
            try {
                ResetCore();
                config = new SdkConfig { Platform = settings.Platform, LoginEnabled = settings.LoginEnabled, StorageEnabled = settings.StorageEnabled, LogCallback = settings.LogCallback };
                auth = provider; storage = storageProvider; dispatch = mainThreadDispatch; fallback = engineLog;
                Log(SdkLogLevel.Info, "SDK.Initialize", Guid.NewGuid().ToString("N"), "Complete", "{}", "Success");
            } finally { changingLifecycle = false; }
        }
        static void EnsureNotLogging()
        {
            if (writingLog) throw new InvalidOperationException("SDK operations cannot be invoked from the log sink");
            if (changingLifecycle) throw new InvalidOperationException("SDK operations cannot be invoked while initializing or disposing providers");
            if (callingStorage) throw new InvalidOperationException("SDK operations cannot be reentered from a storage provider");
        }
        static string Safe(string value) { return (value ?? "").Replace("\r", " ").Replace("\n", " "); }
        static void Log(SdkLogLevel level, string api, string call, string stage, string parameters, string result)
        {
            if (writingLog) return;
            writingLog = true;
            try {
            var sink = config == null ? null : config.LogCallback;
            var backup = fallback;
            string line = DateTimeOffset.Now.ToString("o") + " [" + level + "] platform=" + Safe(config == null ? "Unconfigured" : config.Platform)
                + " interface=" + api + " callId=" + call + " logId=" + Guid.NewGuid().ToString("N")
                + " traceId=" + call + " stage=" + stage + " params=" + parameters + " result=" + Safe(result);
            try { if (sink != null) { sink(level, line); return; } } catch { }
            try { if (backup != null) backup(level, line); } catch { }
            } finally { writingLog = false; }
        }
        public static void Login(Action<LoginResult> callback) { Login(callback, 15000); }
        public static void Login(Action<LoginResult> callback, int timeoutMilliseconds)
        {
            EnsureNotLogging();
            if (timeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException("timeoutMilliseconds");
            if (callback == null) throw new ArgumentNullException("callback");
            if (config == null) { FinishImmediate(callback, Guid.NewGuid().ToString("N"), "SDK.NOT_INITIALIZED"); return; }
            string call = Guid.NewGuid().ToString("N");
            Log(SdkLogLevel.Info, "SDK.Login", call, "Start", "{\"timeoutMilliseconds\":" + timeoutMilliseconds + "}", "Pending");
            if (!config.LoginEnabled) { FinishImmediate(callback, call, "MODULE.DISABLED"); return; }
            if (auth == null) { FinishImmediate(callback, call, "CAPABILITY.NOT_SUPPORTED"); return; }
            if (busy) { FinishImmediate(callback, call, "AUTH.IN_PROGRESS"); return; }
            busy = true;
            int epoch = generation, completed = 0;
            var post = dispatch;
            var timer = Stopwatch.StartNew();
            Action<LoginResult> done = result => {
                if (epoch != generation) return;
                if (Interlocked.Exchange(ref completed, 1) != 0) return;
                post(() => {
                    if (epoch != generation) return;
                    busy = false; cancelLogin = null;
                    checkTimeout = null;
                    result = result ?? LoginResult.Fail("AUTH.UNKNOWN", "Provider returned no result");
                    if (result.Success && string.IsNullOrWhiteSpace(result.Session))
                        result = LoginResult.Fail("AUTH.INVALID_SESSION", "Provider returned an empty game session").WithServerCallId(result.ServerCallId);
                    // Never log session credentials or arbitrary provider messages.
                    Log(result.Success ? SdkLogLevel.Info : SdkLogLevel.Error, "SDK.Login", call, "Complete", "{}",
                        (result.Success ? "Success" : result.Error.Code) + " durationMs=" + timer.ElapsedMilliseconds
                        + " serverCallId=" + (result.ServerCallId ?? "none"));
                    InvokeCallback(callback, result, call);
                });
            };
            cancelLogin = () => done(LoginResult.Fail("AUTH.CANCELLED", "Caller cancelled local login wait"));
            checkTimeout = () => { if (timer.ElapsedMilliseconds >= timeoutMilliseconds) done(LoginResult.Fail("AUTH.TIMEOUT", "Login timed out")); };
            try { auth.Login(done); } catch { done(LoginResult.Fail("AUTH.UNKNOWN", "Provider threw an exception")); }
        }
        static void FinishImmediate(Action<LoginResult> callback, string call, string code)
        {
            Log(SdkLogLevel.Warning, "SDK.Login", call, "Complete", "{}", code);
            InvokeCallback(callback, LoginResult.Fail(code, code), call);
        }
        static void InvokeCallback(Action<LoginResult> callback, LoginResult result, string call)
        {
            result.CallId = call;
            if (result.Error != null) {
                result.Error.CallId = call;
                result.Error.Operation = "SDK.Login";
                if (result.Error.Stage == null) result.Error.Stage = "Login";
            }
            try { callback(result); }
            catch { Log(SdkLogLevel.Error, "SDK.Login", call, "Callback", "{}", "CALLBACK.EXCEPTION"); }
        }
        public static void Tick()
        {
            EnsureNotLogging();
            var check = checkTimeout;
            if (check != null) check();
        }
        public static void CancelLogin()
        {
            EnsureNotLogging();
            var cancel = cancelLogin;
            if (cancel != null) cancel();
        }
        public static StorageResult SetStorageSync(string key, string value) { return StorageCall("SDK.SetStorageSync", key, value, 0); }
        public static StorageResult GetStorageSync(string key) { return StorageCall("SDK.GetStorageSync", key, null, 1); }
        public static StorageResult RemoveStorageSync(string key) { return StorageCall("SDK.RemoveStorageSync", key, null, 2); }
        static StorageResult StorageCall(string api, string key, string value, int operation)
        {
            EnsureNotLogging();
            string call = Guid.NewGuid().ToString("N");
            string parameters = "{\"keyLength\":" + (key == null ? 0 : key.Length) + ",\"valueLength\":" + (value == null ? 0 : value.Length) + "}";
            Log(SdkLogLevel.Info, api, call, "Start", parameters, "Pending");
            StorageResult result;
            if (config == null) result = StorageResult.Fail("SDK.NOT_INITIALIZED", "Initialize SDK before storage calls");
            else if (!config.StorageEnabled) result = StorageResult.Fail("MODULE.DISABLED", "Storage module is disabled");
            else if (storage == null) result = StorageResult.Fail("CAPABILITY.NOT_SUPPORTED", "No storage provider is installed");
            else if (string.IsNullOrWhiteSpace(key) || (operation == 0 && value == null)) result = StorageResult.Fail("STORAGE.INVALID_ARGUMENT", "Storage requires a nonblank key and a non-null value for Set");
            else {
                callingStorage = true;
                try {
                    result = operation == 0 ? storage.Set(key, value) : operation == 1 ? storage.Get(key) : storage.Remove(key);
                    if (result == null || (operation == 1 && result.Success && result.Value == null))
                        result = StorageResult.Fail("STORAGE.INVALID_RESULT", "Storage provider returned an invalid result");
                } catch { result = StorageResult.Fail("STORAGE.PROVIDER_FAILED", "Storage provider failed"); }
                finally { callingStorage = false; }
            }
            result.CallId = call;
            if (result.Error != null) { result.Error.CallId = call; result.Error.Operation = api; result.Error.Stage = "Storage"; }
            Log(result.Success ? SdkLogLevel.Info : SdkLogLevel.Error, api, call, "Complete", parameters, result.Success ? "Success" : result.Error.Code);
            return result;
        }
        public static void Shutdown()
        {
            EnsureNotLogging();
            changingLifecycle = true;
            try { ResetCore(); } finally { changingLifecycle = false; }
        }
        static void ResetCore()
        {
            generation++; busy = false; cancelLogin = null;
            checkTimeout = null;
            var old = auth; var oldStorage = storage; auth = null; storage = null; dispatch = null;
            string call = Guid.NewGuid().ToString("N");
            bool failed = false;
            if (old != null) {
                try { old.Dispose(); }
                catch {
                    failed = true;
                    Log(SdkLogLevel.Error, "SDK.Shutdown", call, "Dispose", "{}", "SDK.DISPOSE_FAILED");
                }
            }
            if (oldStorage != null && !ReferenceEquals(old, oldStorage)) {
                try { oldStorage.Dispose(); }
                catch { failed = true; Log(SdkLogLevel.Error, "SDK.Shutdown", call, "DisposeStorage", "{}", "SDK.DISPOSE_FAILED"); }
            }
            if (config != null) Log(failed ? SdkLogLevel.Warning : SdkLogLevel.Info, "SDK.Shutdown", call, "Complete", "{}", failed ? "CompletedWithDisposeError" : "Success");
            config = null;
        }
    }
}
