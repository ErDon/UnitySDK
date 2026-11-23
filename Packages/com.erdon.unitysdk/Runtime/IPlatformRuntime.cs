using System;
namespace ErDon.UnitySDK
{
    /// <summary>Initializes the official host before constructing game capabilities. Call on the main thread.</summary>
    public interface IPlatformRuntime : IDisposable
    {
        string Platform { get; }
        bool IsReady { get; }
        // Null error means ready. Disposal invalidates outstanding callbacks, not the official host itself.
        void Initialize(Action<SdkError> callback);
    }
}
