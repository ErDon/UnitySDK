using System;
using System.Globalization;
using WeChatWASM;
namespace ErDon.UnitySDK.WeChat
{
    internal interface IWeChatRewardedAd
    {
        event Action Loaded;
        event Action<SdkError> Failed;
        event Action<bool> Closed;
        void Load(); void Show(); void Destroy();
    }
    internal sealed class OfficialWeChatRewardedAd : IWeChatRewardedAd
    {
        readonly WXRewardedVideoAd ad;
        public event Action Loaded;
        public event Action<SdkError> Failed;
        public event Action<bool> Closed;
        public OfficialWeChatRewardedAd(string id) {
            ad=WX.CreateRewardedVideoAd(new WXCreateRewardedVideoAdParam{adUnitId=id,multiton=false});
            if(ad==null)throw new InvalidOperationException("Missing ad instance");
            try { ad.OnError(OnError);ad.OnClose(OnClose); }
            catch { try { Destroy(); } catch { } throw; }
        }
        void OnError(WXADErrorResponse error){var cb=Failed;if(cb!=null)cb(new SdkError("ADS.PLATFORM_FAILED","WeChat ad request failed",error==null?null:error.errCode.ToString(CultureInfo.InvariantCulture),null));}
        void OnClose(WXRewardedVideoAdOnCloseResponse result){
            if(result==null){var fail=Failed;if(fail!=null)fail(new SdkError("ADS.INVALID_RESULT","WeChat returned no close result"));return;}
            var cb=Closed;if(cb!=null)cb(result.isEnded);
        }
        public void Load(){ad.Load(_=>{var cb=Loaded;if(cb!=null)cb();},OnError);}
        public void Show(){ad.Show(null,_=>{var cb=Failed;if(cb!=null)cb(new SdkError("ADS.SHOW_FAILED","WeChat rejected ad display"));});}
        public void Destroy(){try { ad.OffError(OnError); } finally { try { ad.OffClose(OnClose); } finally { ad.Destroy(); } }}
    }
    /// <summary>Single-video flow. Main-thread use after WeChat initialization; no reward is issued here.</summary>
    public sealed class WeChatRewardedAds : IRewardedAds
    {
        readonly Func<string,IWeChatRewardedAd> create;
        IWeChatRewardedAd active;
        Action cleanup;
        bool disposed;
        public WeChatRewardedAds():this(id=>new OfficialWeChatRewardedAd(id)){}
        internal WeChatRewardedAds(Func<string,IWeChatRewardedAd> create){if(create==null)throw new ArgumentNullException("create");this.create=create;}
        static RewardedAdResult Error(string code,string reason){return RewardedAdResult.Fail(new SdkError(code,reason));}
        public void Show(RewardedAdRequest request,Action<RewardedAdResult> callback)
        {
            if(callback==null)throw new ArgumentNullException("callback");
            if(disposed){callback(Error("SDK.DISPOSED","WeChat ads provider was disposed"));return;}
            if(request==null||string.IsNullOrWhiteSpace(request.AdUnitId)){callback(Error("ADS.INVALID_ARGUMENT","Ad unit ID is required"));return;}
            if(active!=null){callback(Error("ADS.IN_PROGRESS","A rewarded ad is already active"));return;}
            IWeChatRewardedAd ad=null;bool finished=false,shown=false;
            Action loaded=null;Action<SdkError> failed=null;Action<bool> closed=null;
            Action release=()=>{
                if(ad==null)return;
                if(ReferenceEquals(active,ad)){active=null;cleanup=null;}
                try{ad.Loaded-=loaded;}catch{}try{ad.Failed-=failed;}catch{}try{ad.Closed-=closed;}catch{}
                try{ad.Destroy();}catch{}
            };
            Action<RewardedAdResult> finish=result=>{if(finished||disposed)return;finished=true;release();callback(result);};
            loaded=()=>{if(finished||disposed||shown)return;shown=true;try{ad.Show();}catch{finish(Error("ADS.SHOW_FAILED","WeChat ad show failed"));}};
            failed=error=>finish(RewardedAdResult.Fail(error ?? new SdkError("ADS.INVALID_RESULT","Missing ad error")));
            closed=complete=>finish(shown?RewardedAdResult.Closed(complete):Error("ADS.INVALID_STATE","Ad closed before display was requested"));
            try {
                ad=create(request.AdUnitId);
                if(ad==null){finish(Error("ADS.CREATE_FAILED","WeChat returned no ad instance"));return;}
                active=ad;cleanup=()=>{finished=true;release();};
                ad.Loaded+=loaded;ad.Failed+=failed;ad.Closed+=closed;ad.Load();
            }catch{finish(Error("ADS.PROVIDER_EXCEPTION","WeChat ad invocation failed"));}
        }
        public void Dispose(){if(disposed)return;disposed=true;var release=cleanup;if(release!=null)release();}
    }
}
