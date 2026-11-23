using System;
namespace ErDon.UnitySDK
{
    public sealed class RewardedAdRequest
    {
        public string AdUnitId {get;private set;}
        public RewardedAdRequest(string adUnitId){AdUnitId=adUnitId;}
    }
    public sealed class RewardedAdResult
    {
        public string CallId {get;internal set;}
        public string TraceId {get{return CallId;}}
        internal RewardedAdResult Snapshot(){var copy=(RewardedAdResult)MemberwiseClone();copy.Error=Error==null?null:Error.Snapshot();return copy;}
        public bool Success {get{return Error==null;}}
        // Client-reported completion only; not proof of server-authorized reward delivery.
        public bool Completed {get;private set;}
        public SdkError Error {get;private set;}
        public static RewardedAdResult Closed(bool completed){return new RewardedAdResult{Completed=completed};}
        public static RewardedAdResult Fail(SdkError error){if(error==null)throw new ArgumentNullException("error");return new RewardedAdResult{Error=error};}
        public static RewardedAdResult Fail(string code,string reason){return Fail(new SdkError(code,reason));}
    }
    public interface IRewardedAds : IDisposable
    {
        void Show(RewardedAdRequest request,Action<RewardedAdResult> callback);
    }
}
