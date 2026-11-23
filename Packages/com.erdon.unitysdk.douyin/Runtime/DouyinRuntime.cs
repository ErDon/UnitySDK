using System;
using System.Globalization;
using System.Threading;
using TTSDK;
namespace ErDon.UnitySDK.Douyin
{
    /// <summary>TTSDK 6.9.7 initialization bridge. The caller owns its lifetime and main-thread scheduling.</summary>
    public sealed class DouyinRuntime : IPlatformRuntime
    {
        readonly Action<Action<int>> initialize;
        bool disposed, initializing;
        public string Platform { get { return "Douyin"; } }
        public bool IsReady { get; private set; }
        public DouyinRuntime() : this(done => TT.InitSDK((code, environment) => done(code))) { }
        internal DouyinRuntime(Action<Action<int>> initialize)
        {
            if(initialize==null)throw new ArgumentNullException("initialize");
            this.initialize=initialize;
        }
        public void Initialize(Action<SdkError> callback)
        {
            if(callback==null)throw new ArgumentNullException("callback");
            if(disposed){callback(new SdkError("SDK.DISPOSED","Douyin runtime bridge was disposed"));return;}
            if(IsReady){callback(null);return;}
            if(initializing){callback(new SdkError("INIT.IN_PROGRESS","Douyin initialization is already pending"));return;}
            initializing=true;int completed=0;
            Action<SdkError> finish=error=>{
                if(disposed || Interlocked.Exchange(ref completed,1)!=0)return;
                initializing=false;IsReady=error==null;callback(error);
            };
            try {initialize(code=>{
                if(code==0){finish(null);return;}
                string reason=code==1?"Douyin SDK version is unsupported":code==2?"Unity engine version is unsupported":"Douyin runtime initialization failed";
                finish(new SdkError(code==1?"INIT.SDK_VERSION_UNSUPPORTED":code==2?"INIT.ENGINE_VERSION_UNSUPPORTED":"INIT.PLATFORM_FAILED",
                    reason,code.ToString(CultureInfo.InvariantCulture),null));
            });}
            catch {finish(new SdkError("INIT.PROVIDER_EXCEPTION","Douyin initialization invocation failed"));}
        }
        public void Dispose(){disposed=true;initializing=false;IsReady=false;}
    }
}
