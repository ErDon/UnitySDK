using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
namespace ErDon.UnitySDK
{
    public interface INetworkProviderFactory { INetwork CreateNetwork(); }
    public static partial class SDK
    {
        static INetwork network;
        static readonly HashSet<NetworkCall> networkCalls=new HashSet<NetworkCall>();
        sealed class NetworkCall : INetworkOperation
        {
            internal readonly string Id=Guid.NewGuid().ToString("N");
            internal readonly int Epoch=generation;
            internal readonly Stopwatch Timer=Stopwatch.StartNew();
            internal int Timeout;
            internal Action<NetworkResult> Callback;
            internal Action<Action> Post;
            INetworkOperation operation;
            int completed;
            bool abort;
            internal void Attach(INetworkOperation value)
            {
                operation=value;
                if(abort||Epoch!=generation)Abort();
                else if(Volatile.Read(ref completed)!=0)operation=null;
            }
            internal bool Complete(NetworkResult result,bool cancelOperation=false)
            {
                if(Epoch!=generation||Interlocked.Exchange(ref completed,1)!=0)return false;
                var snapshot=result==null?NetworkResult.Fail("NETWORK.INVALID_RESPONSE","Network provider returned no result"):result.Snapshot();
                if(cancelOperation)Abort();
                var post=Post;Post=null;
                PostCompletion(post,Epoch,Id,"SDK.Request",()=>{
                    networkCalls.Remove(this);operation=null;
                    var callback=Callback;Callback=null;
                    FinishNetwork(callback,snapshot,Id);
                });
                return true;
            }
            void Abort()
            {
                abort=true;var current=operation;operation=null;
                if(current!=null)try{current.Cancel();}catch{Log(SdkLogLevel.Warning,"SDK.Request",Id,"Cancel","{}","NETWORK.CANCEL_FAILED");}
            }
            public void Cancel()
            {
                EnsureNotLogging();
                Complete(NetworkResult.Fail("NETWORK.CANCELLED","Caller cancelled network request"),true);
            }
            internal void Tick(){if(Timer.ElapsedMilliseconds>=Timeout)Complete(NetworkResult.Fail("NETWORK.TIMEOUT","Network request timed out"),true);}
            internal void Abandon(){Interlocked.Exchange(ref completed,1);Callback=null;Post=null;Abort();}
        }
        sealed class FinishedNetworkCall : INetworkOperation {public void Cancel() {}}
        public static INetworkOperation Request(NetworkRequest request,Action<NetworkResult> callback)
        {
            EnsureNotLogging();if(callback==null)throw new ArgumentNullException("callback");
            var call=new NetworkCall{Callback=callback,Post=dispatch,Timeout=request==null?0:request.TimeoutSeconds*1000};
            Log(SdkLogLevel.Info,"SDK.Request",call.Id,"Start","{\"content\":\"[REDACTED]\"}","Pending");
            string invalid=config==null?"SDK.NOT_INITIALIZED":!config.NetworkEnabled?"MODULE.DISABLED":network==null?"CAPABILITY.NOT_SUPPORTED":request==null?"NETWORK.INVALID_ARGUMENT":null;
            if(invalid!=null){FinishNetwork(callback,NetworkResult.Fail(invalid,invalid),call.Id);return new FinishedNetworkCall();}
            networkCalls.Add(call);
            try {
                var operation=network.Send(request,result=>call.Complete(result));
                call.Attach(operation);
                if(operation==null)call.Complete(NetworkResult.Fail("NETWORK.INVALID_OPERATION","Network provider returned no cancellation handle"));
            }catch{call.Complete(NetworkResult.Fail("NETWORK.PROVIDER_FAILED","Network provider invocation failed"));}
            return call;
        }
        static void FinishNetwork(Action<NetworkResult> callback,NetworkResult result,string call)
        {
            result=result.Snapshot();result.CallId=call;
            if(result.Error!=null){result.Error.CallId=call;result.Error.Operation="SDK.Request";result.Error.Stage="Network";}
            Log(result.Success?SdkLogLevel.Info:SdkLogLevel.Error,"SDK.Request",call,"Complete","{}",result.Success?"HTTP."+result.StatusCode:result.Error.Code);
            try{callback(result);}catch{Log(SdkLogLevel.Error,"SDK.Request",call,"Callback","{}","CALLBACK.EXCEPTION");}
        }
        static void TickNetwork(){foreach(var call in new List<NetworkCall>(networkCalls))call.Tick();}
        static void ResetNetworkCalls(){var old=new List<NetworkCall>(networkCalls);networkCalls.Clear();foreach(var call in old)call.Abandon();}
    }
}
