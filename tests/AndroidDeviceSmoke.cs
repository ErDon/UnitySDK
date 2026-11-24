using System;
using System.Threading;
using ErDon.UnitySDK;
using ErDon.UnitySDK.Android;
using UnityEngine;

// Device-only fixture; never included in the shipped UPM package.
public sealed class AndroidDeviceSmoke : MonoBehaviour
{
    AndroidBridge bridge;
    void Start() {
        var config=new SdkRuntimeConfiguration{platform="Android",gameId="android-smoke",appId="com.erdon.sdk.smoke",login=false,storage=true};
        UnitySdkHost.BeginConfiguredStartup(config,null,result=> {
            try {
                Check(result.Success,result.Error==null?"startup":result.Error.Code);
                var prior=SDK.GetStorageSync("restart-sentinel");
                if(prior.Success&&prior.Value=="persisted")Debug.Log("ANDROID_DEVICE_PERSISTENCE_PASS");
                else Check(prior.Error!=null&&prior.Error.Code=="STORAGE.NOT_FOUND","unexpected persistent value");
                Check(SDK.SetStorageSync("restart-sentinel","persisted").Success,"persist write");
                Check(SDK.SetStorageSync("unicode","中文\n\"x").Success,"unicode write");
                Check(SDK.GetStorageSync("unicode").Value=="中文\n\"x","unicode read");
                Check(SDK.SetStorageSync("empty","").Success&&SDK.GetStorageSync("empty").Value=="","empty value");
                Check(SDK.RemoveStorageSync("unicode").Success&&SDK.GetStorageSync("unicode").Error.Code=="STORAGE.NOT_FOUND","remove");
                int owner=Thread.CurrentThread.ManagedThreadId;var context=SynchronizationContext.Current;
                bridge=new AndroidBridge("android-smoke","test","ADS",action=>context.Post(_=>action(),null));
                bridge.Call("ads","{\"unitId\":\"test-only\"}",reply=> {
                    try {
                        Check(Thread.CurrentThread.ManagedThreadId==owner,"callback thread");
                        Check(reply.code=="CAPABILITY.NOT_SUPPORTED","missing vendor must not fake success");
                        bridge.Dispose();
                        bridge=new AndroidBridge("android-smoke","test","NETWORK",action=>context.Post(_=>action(),null));
                        bridge.Call("network","{\"url\":\"http://blocked.example\",\"method\":\"GET\",\"body\":\"\"}",network=> {
                            try {
                                Check(Thread.CurrentThread.ManagedThreadId==owner,"network callback thread");
                                Check(network.code=="NETWORK.INVALID_REQUEST","default network provider must reject cleartext before dispatch");
                                bridge.Dispose();bridge=null;UnitySdkHost.Shutdown();
                                Debug.Log("ANDROID_DEVICE_JNI_PASS: IL2CPP, R8, protocol, storage, callback thread, unsupported channel, network validation and shutdown");
                            }catch(Exception error){Debug.LogError("ANDROID_DEVICE_FAIL: "+error);}
                        });
                    } catch(Exception error){Debug.LogError("ANDROID_DEVICE_FAIL: "+error);}
                });
            } catch(Exception error){Debug.LogError("ANDROID_DEVICE_FAIL: "+error);}
        });
    }
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    void OnDestroy(){if(bridge!=null)bridge.Dispose();UnitySdkHost.Shutdown();}
}
