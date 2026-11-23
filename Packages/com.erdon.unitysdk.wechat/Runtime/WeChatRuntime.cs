using System;
using System.Globalization;
using System.Threading;
using WeChatWASM;
namespace ErDon.UnitySDK.WeChat
{
    /// <summary>WeChat 0.1.23 initialization bridge. The caller owns its lifetime and main-thread scheduling.</summary>
    public sealed class WeChatRuntime : IPlatformRuntime
    {
        readonly Action<Action<int>> initialize;
        bool disposed, initializing;
        public string Platform { get { return "WeChat"; } }
        public bool IsReady { get; private set; }
        public WeChatRuntime() : this(done => WX.InitSDK(done)) { }
        internal WeChatRuntime(Action<Action<int>> initialize)
        {
            if(initialize==null)throw new ArgumentNullException("initialize");
            this.initialize=initialize;
        }
        public void Initialize(Action<SdkError> callback)
        {
            if(callback==null)throw new ArgumentNullException("callback");
            if(disposed){callback(new SdkError("SDK.DISPOSED","WeChat runtime bridge was disposed"));return;}
            if(IsReady){callback(null);return;}
            if(initializing){callback(new SdkError("INIT.IN_PROGRESS","WeChat initialization is already pending"));return;}
            initializing=true;int completed=0;
            Action<SdkError> finish=error=>{
                if(disposed || Interlocked.Exchange(ref completed,1)!=0)return;
                initializing=false;IsReady=error==null;callback(error);
            };
            try {initialize(code=>{
                if(code==200){finish(null);return;}
                finish(new SdkError("INIT.PLATFORM_FAILED", "WeChat runtime initialization failed",
                    code.ToString(CultureInfo.InvariantCulture), null));
            });}
            catch {finish(new SdkError("INIT.PROVIDER_EXCEPTION","WeChat initialization invocation failed"));}
        }
        public void Dispose(){disposed=true;initializing=false;IsReady=false;}
    }
}
