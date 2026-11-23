using System;
using System.Collections.Generic;
using System.Text;
namespace ErDon.UnitySDK.Editor
{
    public enum MockNetworkOutcome { HttpOk, HttpUnauthorized, ConnectionFailed, Timeout }
    public sealed class EditorMockNetwork : INetwork
    {
        sealed class Operation : INetworkOperation
        {
            internal EditorMockNetwork Owner;
            internal Action<NetworkResult> Callback;
            internal void Complete(NetworkResult result){var cb=Callback;Callback=null;Owner.pending.Remove(this);if(cb!=null&&!Owner.disposed)cb(result);}
            public void Cancel(){Complete(NetworkResult.Fail("NETWORK.CANCELLED","Mock request cancelled"));}
        }
        readonly MockNetworkOutcome outcome;
        readonly HashSet<Operation> pending=new HashSet<Operation>();
        bool disposed;
        public EditorMockNetwork(MockNetworkOutcome outcome){this.outcome=outcome;}
        public INetworkOperation Send(NetworkRequest request,Action<NetworkResult> callback)
        {
            if(request==null||callback==null)throw new ArgumentNullException();
            var operation=new Operation{Owner=this,Callback=callback};
            if(disposed){operation.Callback=null;callback(NetworkResult.Fail("SDK.DISPOSED","Mock network disposed"));return operation;}
            pending.Add(operation);
            if(outcome==MockNetworkOutcome.Timeout)return operation;
            if(outcome==MockNetworkOutcome.ConnectionFailed)operation.Complete(NetworkResult.Fail("NETWORK.UNAVAILABLE","Mock connection failure"));
            else {
                var body=Encoding.UTF8.GetBytes(outcome==MockNetworkOutcome.HttpOk?"{\"mock\":true}":"{\"error\":\"unauthorized\"}");
                operation.Complete(body.Length>request.MaxResponseBytes?NetworkResult.Fail("NETWORK.RESPONSE_TOO_LARGE","Mock response exceeds configured limit"):
                    NetworkResult.Response(outcome==MockNetworkOutcome.HttpOk?200:401,body,new Dictionary<string,string>{{"Content-Type","application/json"}}));
            }
            return operation;
        }
        public void Dispose(){if(disposed)return;disposed=true;foreach(var operation in pending)operation.Callback=null;pending.Clear();}
    }
}
