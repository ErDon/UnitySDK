#if !ERDON_EXCLUDE_ADS || UNITY_EDITOR
using System;
using System.Globalization;
using TTSDK;
namespace ErDon.UnitySDK.Douyin
{
    internal interface IDouyinRewardedAd
    {
        event Action Loaded;
        event Action<int> Failed;
        event Action<bool> Closed;
        void Load(); void Show(); void Destroy();
    }
    internal sealed class OfficialDouyinRewardedAd : IDouyinRewardedAd
    {
        readonly TTRewardedVideoAd ad;
        public event Action Loaded;
        public event Action<int> Failed;
        public event Action<bool> Closed;
        public OfficialDouyinRewardedAd(string id) {
            ad=TT.CreateRewardedVideoAd(new CreateRewardedVideoAdParam{AdUnitId=id,Multiton=false});
            if(ad==null)throw new InvalidOperationException("Missing ad instance");
            ad.OnLoad+=OnLoad;ad.OnError+=OnError;ad.OnClose+=OnClose;
        }
        void OnLoad(){var cb=Loaded;if(cb!=null)cb();}
        void OnError(int code,string message){var cb=Failed;if(cb!=null)cb(code);}
        void OnClose(bool complete,int count){var cb=Closed;if(cb!=null)cb(complete);}
        public void Load(){ad.Load();} public void Show(){ad.Show();}
        public void Destroy(){ad.OnLoad-=OnLoad;ad.OnError-=OnError;ad.OnClose-=OnClose;ad.Destroy();}
    }
    /// <summary>Single-video flow. Main-thread use after TTSDK initialization; no reward is issued here.</summary>
    public sealed class DouyinRewardedAds : IRewardedAds
    {
        readonly Func<string,IDouyinRewardedAd> create;
        IDouyinRewardedAd active;
        Action cleanup;
        bool disposed;
        public DouyinRewardedAds():this(id=>new OfficialDouyinRewardedAd(id)){}
        internal DouyinRewardedAds(Func<string,IDouyinRewardedAd> create){if(create==null)throw new ArgumentNullException("create");this.create=create;}
        static RewardedAdResult Error(string code,string reason){return RewardedAdResult.Fail(new SdkError(code,reason));}
        public void Show(RewardedAdRequest request,Action<RewardedAdResult> callback)
        {
            if(callback==null)throw new ArgumentNullException("callback");
            if(disposed){callback(Error("SDK.DISPOSED","Douyin ads provider was disposed"));return;}
            if(request==null||string.IsNullOrWhiteSpace(request.AdUnitId)){callback(Error("ADS.INVALID_ARGUMENT","Ad unit ID is required"));return;}
            if(active!=null){callback(Error("ADS.IN_PROGRESS","A rewarded ad is already active"));return;}
            IDouyinRewardedAd ad=null;bool finished=false,shown=false;
            Action loaded=null;Action<int> failed=null;Action<bool> closed=null;
            Action release=()=>{
                if(ad==null)return;
                if(ReferenceEquals(active,ad)){active=null;cleanup=null;}
                try{ad.Loaded-=loaded;}catch{}try{ad.Failed-=failed;}catch{}try{ad.Closed-=closed;}catch{}
                try{ad.Destroy();}catch{}
            };
            Action<RewardedAdResult> finish=result=>{if(finished||disposed)return;finished=true;release();callback(result);};
            loaded=()=>{if(finished||disposed||shown)return;shown=true;try{ad.Show();}catch{finish(Error("ADS.SHOW_FAILED","Douyin ad show failed"));}};
            failed=code=>finish(RewardedAdResult.Fail(new SdkError("ADS.PLATFORM_FAILED","Douyin ad request failed",code.ToString(CultureInfo.InvariantCulture),null)));
            closed=complete=>finish(shown?RewardedAdResult.Closed(complete):Error("ADS.INVALID_STATE","Ad closed before display was requested"));
            try {
                ad=create(request.AdUnitId);
                if(ad==null){finish(Error("ADS.CREATE_FAILED","Douyin returned no ad instance"));return;}
                active=ad;cleanup=()=>{finished=true;release();};
                ad.Loaded+=loaded;ad.Failed+=failed;ad.Closed+=closed;ad.Load();
            }catch{finish(Error("ADS.PROVIDER_EXCEPTION","Douyin ad invocation failed"));}
        }
        public void Dispose(){if(disposed)return;disposed=true;var release=cleanup;if(release!=null)release();}
    }
}

#endif
