using System;
using System.Threading;
namespace ErDon.UnitySDK
{
    public sealed class PlatformCredential
    {
        public string Platform { get; private set; }
        public string Code { get; private set; }
        public PlatformCredential(string platform, string code) { Platform=platform; Code=code; }
        public override string ToString() { return "PlatformCredential([REDACTED])"; }
    }
    public interface IPlatformCredentialProvider : IDisposable
    {
        void GetCredential(Action<PlatformCredential, SdkError> callback);
    }
    public interface ISessionExchange : IDisposable
    {
        void Exchange(PlatformCredential credential, Action<LoginResult> callback);
    }
    // Composes platform authorization and backend verification; owns both dependencies.
    public sealed class VerifiedAuth : IAuth
    {
        readonly IPlatformCredentialProvider provider;
        readonly ISessionExchange exchange;
        readonly Action<Action> post;
        bool disposed;
        public VerifiedAuth(IPlatformCredentialProvider provider, ISessionExchange exchange, Action<Action> mainThreadPost)
        {
            if(provider==null || exchange==null || mainThreadPost==null) throw new ArgumentNullException();
            this.provider=provider;this.exchange=exchange;post=mainThreadPost;
        }
        public void Login(Action<LoginResult> callback)
        {
            if(disposed) { callback(LoginResult.Fail("SDK.DISPOSED","Auth was disposed"));return; }
            int received=0, completed=0;
            Action<LoginResult> finish = result => {
                if(Interlocked.Exchange(ref completed,1)!=0)return;
                post(()=>{if(!disposed)callback(result ?? LoginResult.Fail("AUTH.UNKNOWN","Empty exchange result"));});
            };
            Action<PlatformCredential,SdkError> authorized = (credential,error)=> {
                if(Interlocked.Exchange(ref received,1)!=0)return;
                post(()=> {
                    if(disposed)return;
                    if(error!=null){error.Stage="PlatformAuthorization";finish(LoginResult.Fail(error));return;}
                    if(credential==null || string.IsNullOrWhiteSpace(credential.Code) || string.IsNullOrWhiteSpace(credential.Platform)) {
                        finish(LoginResult.Fail(new SdkError("AUTH.INVALID_CREDENTIAL","Platform returned no credential"){Stage="PlatformAuthorization"}));return;
                    }
                    try {
                        exchange.Exchange(credential,result=>{
                            if(result!=null && result.Error!=null)result.Error.Stage="SessionExchange";
                            if(result!=null && result.Success && string.IsNullOrWhiteSpace(result.Session))
                                result=LoginResult.Fail(new SdkError("AUTH.INVALID_SESSION","Backend returned no session"){Stage="SessionExchange"});
                            finish(result);
                        });
                    }catch{finish(LoginResult.Fail(new SdkError("AUTH.SERVER_REJECTED","Session exchange failed"){Stage="SessionExchange"}));}
                });
            };
            try{provider.GetCredential(authorized);}catch{authorized(null,new SdkError("AUTH.UNKNOWN","Platform authorization failed"));}
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            try{provider.Dispose();}finally{exchange.Dispose();}
        }
    }
}
