using System;
using System.Globalization;
using System.Threading;
using TTSDK;
namespace ErDon.UnitySDK.TikTok
{
    /// <summary>TikTok 1.1.6-Release initialization bridge. The caller owns its lifetime and main-thread scheduling.</summary>
    public sealed class TikTokRuntime : IPlatformRuntime
    {
        readonly Action<Action<int>> initialize;
        bool disposed, initializing;
        public string Platform { get { return "TikTok"; } }
        public bool IsReady { get; private set; }
        public TikTokRuntime() : this(done => TT.InitSDK((code, environment) => done(code))) { }
        internal TikTokRuntime(Action<Action<int>> initialize)
        {
            if(initialize==null)throw new ArgumentNullException("initialize");
            this.initialize=initialize;
        }
        public void Initialize(Action<SdkError> callback)
        {
            if(callback==null)throw new ArgumentNullException("callback");
            if(disposed){callback(new SdkError("SDK.DISPOSED","TikTok runtime bridge was disposed"));return;}
            if(IsReady){callback(null);return;}
            if(initializing){callback(new SdkError("INIT.IN_PROGRESS","TikTok initialization is already pending"));return;}
            initializing=true;int completed=0;
            Action<SdkError> finish=error=>{
                if(disposed || Interlocked.Exchange(ref completed,1)!=0)return;
                initializing=false;IsReady=error==null;callback(error);
            };
            try {initialize(code=>{
                if(code==0){finish(null);return;}
                finish(new SdkError("INIT.PLATFORM_FAILED", "TikTok runtime initialization failed",
                    code.ToString(CultureInfo.InvariantCulture), null));
            });}
            catch {finish(new SdkError("INIT.PROVIDER_EXCEPTION","TikTok initialization invocation failed"));}
        }
        public void Dispose(){disposed=true;initializing=false;IsReady=false;}
    }
}
