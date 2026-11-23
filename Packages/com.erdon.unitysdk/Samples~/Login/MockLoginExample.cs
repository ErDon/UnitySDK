#if UNITY_EDITOR
using System;
using UnityEngine;
using ErDon.UnitySDK;
public sealed class MockLoginExample : MonoBehaviour
{
    sealed class MockAuth : IAuth
    {
        public void Login(Action<LoginResult> callback) { callback(LoginResult.Ok("mock-session-not-real")); }
        public void Dispose() { }
    }
    void Start()
    {
        UnitySdkHost.Initialize(new SdkConfig { Platform = "Mock" }, new MockAuth());
        SDK.Login(result => Debug.Log(result.Success ? "Mock login succeeded (not a real account)" : result.Error.Code));
    }
}
#endif
