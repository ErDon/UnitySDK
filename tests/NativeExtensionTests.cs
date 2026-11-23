using System;
using ErDon.UnitySDK;

// Contract test only: no Android/iOS runtime or vendor SDK is installed here.
class NativeExtensionTests
{
    sealed class Runtime : IPlatformRuntime
    {
        public string Platform { get; private set; }
        public bool IsReady { get; private set; }
        public int Releases;
        public Runtime(string platform) { Platform = platform; }
        public void Initialize(Action<SdkError> callback) { IsReady = true; callback(null); }
        public void Dispose() { Releases++; } // Release bridge observation, not vendor host.
    }
    sealed class Storage : IStorage
    {
        public int Disposals;
        public StorageResult Set(string key, string value) { return StorageResult.Ok(); }
        public StorageResult Get(string key) { return StorageResult.Ok("contract-value"); }
        public StorageResult Remove(string key) { return StorageResult.Ok(); }
        public void Dispose() { Disposals++; }
    }
    sealed class Factory : IPlatformSdkProviderFactory
    {
        public string Platform { get; private set; }
        public int Creates;
        public Storage Last;
        public Factory(string platform) { Platform = platform; }
        public IAuth CreateAuth(Action<Action> dispatch) { throw new Exception("Login was disabled"); }
        public IStorage CreateStorage() { Creates++; return Last = new Storage(); }
    }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Main()
    {
        foreach (var platform in new[] { "Android.ExampleChannel", "iOS.ExampleChannel" })
        {
            var runtime = new Runtime(platform); var factory = new Factory(platform);
            StartupResult result = null;
            using (var startup = new SdkStartup(new SdkConfig { Platform = platform, LoginEnabled = false, StorageEnabled = true },
                runtime, factory, action => action(), (level, text) => {}, value => result = value))
            {
                Check(factory.Creates == 0, "construction before readiness"); startup.Start();
                Check(factory.Creates == 0 && result == null, "startup must complete on Tick"); startup.Tick();
                Check(result != null && result.Success && runtime.Releases == 1 && factory.Creates == 1, "custom identity startup");
                Check(SDK.ActivePlatform == platform && SDK.GetStorageSync("key").Value == "contract-value", "aggregate polymorphic dispatch");
                Check(SDK.ReportEvent(new AnalyticsEvent("event")).Error.Code == "MODULE.DISABLED", "optional capabilities stay disabled");
            }
            SDK.Shutdown(); Check(factory.Last.Disposals == 1 && runtime.Releases == 1, "ownership");
            SDK.InitializeWithFactory(new SdkConfig { Platform = platform, LoginEnabled = false }, factory, action => action(), (level, text) => {});
            Check(factory.Creates == 1, "disabled storage is not created"); SDK.Shutdown();
            bool rejected = false;
            try { new SdkStartup(new SdkConfig { Platform = platform }, new Runtime("different"), factory, a => a(), (l,s) => {}, r => {}); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "identity mismatch rejected");
            var configuration = new SdkRuntimeConfiguration { platform = platform, gameId = "game", appId = "app", login = false };
            Check(configuration.Validate().Contains("CONFIG.PLATFORM_UNSUPPORTED"), "generic extension must not silently enable native builds");
        }
        Console.WriteLine("PASS: native extension contract identities, startup, polymorphism, disabled construction and ownership; no native vendor integration");
    }
}
