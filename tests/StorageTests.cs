using System;
using System.Collections.Generic;
using ErDon.UnitySDK;
class StorageTests
{
    sealed class Store : IStorage {
        public int Calls, Disposes;
        public bool Throw;
        readonly Dictionary<string,string> data=new Dictionary<string,string>();
        public StorageResult Set(string k,string v){Calls++;if(Throw)throw new Exception("SECRET");data[k]=v;return StorageResult.Ok();}
        public StorageResult Get(string k){Calls++;string v;return data.TryGetValue(k,out v)?StorageResult.Ok(v):StorageResult.Fail("STORAGE.NOT_FOUND","Missing key");}
        public StorageResult Remove(string k){Calls++;data.Remove(k);return StorageResult.Ok();}
        public void Dispose(){Disposes++;}
    }
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Main(){
        var logs=new List<string>();var store=new Store();
        Check(SDK.GetStorageSync("a").Error.Code=="SDK.NOT_INITIALIZED","uninitialized");
        SDK.Initialize(new SdkConfig(),null,a=>a(),(l,s)=>logs.Add(s),store);
        Check(SDK.SetStorageSync("secret-key","SECRET").Error.Code=="MODULE.DISABLED"&&store.Calls==0,"disabled does not invoke provider");
        SDK.Initialize(new SdkConfig{StorageEnabled=true},null,a=>a(),(l,s)=>logs.Add(s));
        Check(store.Disposes==1&&SDK.GetStorageSync("a").Error.Code=="CAPABILITY.NOT_SUPPORTED","missing provider and disposal");
        store=new Store();var config=new SdkConfig{StorageEnabled=true};
        SDK.Initialize(config,null,a=>a(),(l,s)=>logs.Add(s),store);config.StorageEnabled=false;
        Check(SDK.SetStorageSync("secret-key","").Success,"empty value valid and config snapshotted");
        Check(SDK.GetStorageSync("secret-key").Value=="","empty and missing distinct");
        Check(SDK.SetStorageSync("secret-key","SECRET").Success,"write");
        Check(SDK.GetStorageSync("secret-key").Value=="SECRET","round trip");
        Check(SDK.SetStorageSync("", "v").Error.Code=="STORAGE.INVALID_ARGUMENT","blank key");
        Check(SDK.SetStorageSync("k", null).Error.Code=="STORAGE.INVALID_ARGUMENT","null value");
        SDK.RemoveStorageSync("secret-key");Check(SDK.RemoveStorageSync("secret-key").Success,"remove idempotent");
        var missing=SDK.GetStorageSync("secret-key");Check(missing.Error.Code=="STORAGE.NOT_FOUND"&&missing.CallId==missing.Error.CallId,"missing correlation");
        store.Throw=true;Check(SDK.SetStorageSync("k","v").Error.Code=="STORAGE.PROVIDER_FAILED","provider exception");
        Check(!string.Join("",logs).Contains("SECRET")&&!string.Join("",logs).Contains("secret-key"),"payload redaction");
        SDK.Shutdown();Check(store.Disposes==1,"storage disposed");
        Console.WriteLine("PASS: storage facade, feature switch, polymorphic dispatch, redaction and ownership");
    }
}
