using System;
using ErDon.UnitySDK;
class FactoryTests
{
    sealed class Provider : IAuth, IStorage {
        public int Disposals;
        public void Login(Action<LoginResult> callback) { callback(LoginResult.Ok("session")); }
        public StorageResult Set(string k,string v) { return StorageResult.Ok(); }
        public StorageResult Get(string k) { return StorageResult.Ok("value"); }
        public StorageResult Remove(string k) { return StorageResult.Ok(); }
        public void Dispose() { Disposals++; }
    }
    sealed class Factory : ISdkProviderFactory {
        public Func<IAuth> Auth;
        public Func<IStorage> Storage;
        public IAuth CreateAuth(Action<Action> post) { return Auth(); }
        public IStorage CreateStorage() { return Storage(); }
    }
    static void Check(bool value,string reason) { if(!value)throw new Exception(reason); }
    static void Start(SdkConfig config,Factory factory) { SDK.InitializeWithFactory(config,factory,a=>a(),(l,m)=>{}); }
    static void Reject(Action action) { bool failed=false;try{action();}catch(InvalidOperationException){failed=true;}Check(failed,"Must reject"); }
    static void Main() {
        Start(new SdkConfig{LoginEnabled=false,StorageEnabled=false},new Factory());
        LoginResult result=null;SDK.Login(r=>result=r);Check(result.Error.Code=="MODULE.DISABLED","Disabled factory never called");
        var old=new Provider();SDK.Initialize(new SdkConfig(),old,a=>a(),(l,m)=>{});
        var partial=new Provider();
        Reject(()=>Start(new SdkConfig{StorageEnabled=true},new Factory{Auth=()=>partial,Storage=()=>{throw new InvalidOperationException();}}));
        Check(partial.Disposals==1&&old.Disposals==0,"Rollback preserves previous instance");
        SDK.Login(r=>result=r);Check(result.Success,"Previous auth still usable");
        Reject(()=>Start(new SdkConfig(),new Factory{Auth=()=>old}));
        Check(old.Disposals==0,"Reused active provider not disposed");
        Reject(()=>Start(new SdkConfig(),new Factory{Auth=()=>null}));
        Reject(()=>Start(new SdkConfig(),new Factory{Auth=()=>{SDK.Shutdown();return new Provider();}}));
        Check(old.Disposals==0,"Factory cannot reenter lifecycle");
        var both=new Provider();
        Start(new SdkConfig{StorageEnabled=true},new Factory{Auth=()=>both,Storage=()=>both});
        Check(old.Disposals==1&&SDK.GetStorageSync("key").Value=="value","Replacement installed");
        SDK.Shutdown();Check(both.Disposals==1,"Shared provider disposed exactly once");
        Console.WriteLine("PASS: configuration-driven factory, rollback, disabled modules and ownership");
    }
}
