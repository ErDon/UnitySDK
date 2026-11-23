using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
namespace ErDon.UnitySDK
{
    // Main-thread provider. Dispose suppresses callbacks; Cancel reports cancellation once.
    public sealed class UnityNetwork : INetwork
    {
        sealed class Download : DownloadHandlerScript
        {
            internal readonly BoundedResponseBuffer Buffer;
            internal Download(int limit):base(new byte[8192]){Buffer=new BoundedResponseBuffer(limit);}
            protected override bool ReceiveData(byte[] data,int length){return data!=null&&Buffer.Append(data,length);}
            protected override byte[] GetData(){return Buffer.ToArray();}
        }
        sealed class Operation : INetworkOperation
        {
            internal UnityWebRequest Request;
            internal Download Download;
            internal bool Finished;
            internal Action<NetworkResult> Callback;
            internal UnityNetwork Owner;
            public void Cancel(){if(Finished)return;if(Request!=null)Request.Abort();Complete(NetworkResult.Fail("NETWORK.CANCELLED","Caller cancelled network request"));}
            internal void Complete(NetworkResult result){if(Finished)return;Finished=true;Owner.active.Remove(this);try{if(!Owner.disposed)Callback(result);}finally{Cleanup();}}
            internal void Cleanup(){if(Request!=null){Request.Dispose();Request=null;}if(Download!=null){Download.Buffer.Dispose();Download=null;}}
        }
        readonly MonoBehaviour host;
        readonly HashSet<Operation> active=new HashSet<Operation>();
        bool disposed;
        public UnityNetwork(MonoBehaviour host){if(host==null)throw new ArgumentNullException("host");this.host=host;}
        public INetworkOperation Send(NetworkRequest input,Action<NetworkResult> callback)
        {
            if(input==null||callback==null)throw new ArgumentNullException();
            var operation=new Operation{Owner=this,Callback=callback};
            if(disposed){operation.Finished=true;callback(NetworkResult.Fail("SDK.DISPOSED","Network provider was disposed"));return operation;}
            if(host==null||!host.isActiveAndEnabled){operation.Complete(NetworkResult.Fail("NETWORK.HOST_UNAVAILABLE","Network host is inactive"));return operation;}
            try {
                operation.Request=new UnityWebRequest(input.Url,input.Method);
                operation.Download=new Download(input.MaxResponseBytes);
                operation.Request.downloadHandler=operation.Download;
                var body=input.Body;if(body.Length!=0)operation.Request.uploadHandler=new UploadHandlerRaw(body);
                operation.Request.timeout=input.TimeoutSeconds;operation.Request.redirectLimit=0;
                foreach(var header in input.Headers)operation.Request.SetRequestHeader(header.Key,header.Value);
                active.Add(operation);
                host.StartCoroutine(Send(operation));
            }catch{operation.Complete(NetworkResult.Fail("NETWORK.PROVIDER_FAILED","Network invocation failed"));}
            return operation;
        }
        IEnumerator Send(Operation operation)
        {
            UnityWebRequestAsyncOperation pending=null;
            try{pending=operation.Request.SendWebRequest();}
            catch{operation.Complete(NetworkResult.Fail("NETWORK.PROVIDER_FAILED","Network request could not start"));}
            if(pending==null)yield break;
            yield return pending;
            if(operation.Finished)yield break;
            var request=operation.Request;
            NetworkResult result;
            if(operation.Download.Buffer.Exceeded)result=NetworkResult.Fail("NETWORK.RESPONSE_TOO_LARGE","Response exceeded configured byte limit");
            else if(request.result==UnityWebRequest.Result.ConnectionError)
                result=NetworkResult.Fail("NETWORK.UNAVAILABLE","Connection failed or timed out");
            else if(request.result==UnityWebRequest.Result.DataProcessingError)
                result=NetworkResult.Fail("NETWORK.INVALID_RESPONSE","Response processing failed");
            else if(request.responseCode<100||request.responseCode>599)
                result=NetworkResult.Fail("NETWORK.INVALID_RESPONSE","No valid HTTP status was returned");
            else result=NetworkResult.Response(request.responseCode,operation.Download.Buffer.ToArray(),request.GetResponseHeaders());
            operation.Complete(result);
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            foreach(var operation in new List<Operation>(active))operation.Cancel();
            active.Clear();
        }
    }
}
