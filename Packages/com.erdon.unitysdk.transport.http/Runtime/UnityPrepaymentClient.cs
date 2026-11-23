#if !ERDON_EXCLUDE_PAYMENTS || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
namespace ErDon.UnitySDK
{
    /// <summary>Session-bound client; replace on account/session change. Main-thread use only.</summary>
    public sealed class UnityPrepaymentClient : IPrepaymentClient
    {
        readonly MonoBehaviour host; readonly string endpoint;
        string session; bool disposed;
        readonly HashSet<UnityWebRequest> active=new HashSet<UnityWebRequest>();
        public UnityPrepaymentClient(MonoBehaviour host,string ordersEndpoint,string gameSession){
            if(host==null)throw new ArgumentNullException("host");
            Uri uri;if(!Uri.TryCreate(ordersEndpoint,UriKind.Absolute,out uri)||uri.Scheme!="https"||!string.IsNullOrEmpty(uri.UserInfo)||
                !string.IsNullOrEmpty(uri.Query)||!string.IsNullOrEmpty(uri.Fragment))throw new ArgumentException("Orders endpoint must be HTTPS without credentials, query or fragment");
            if(gameSession==null||!Regex.IsMatch(gameSession,"\\A[A-Za-z0-9_-]{43}\\z"))throw new ArgumentException("Invalid game session");
            this.host=host;endpoint=uri.AbsoluteUri.TrimEnd('/');session=gameSession;
        }
        public void Prepare(string orderId,Action<PrepaymentResult> callback){
            if(callback==null)throw new ArgumentNullException("callback");
            if(disposed){callback(Fail("SDK.DISPOSED","Prepayment client was disposed"));return;}
            Guid id;if(!Guid.TryParseExact(orderId,"D",out id)){callback(Fail("PAY.INVALID_REQUEST","Invalid game order ID"));return;}
            if(host==null||!host.isActiveAndEnabled){callback(Fail("NETWORK.HOST_UNAVAILABLE","Prepayment host is inactive or destroyed"));return;}
            host.StartCoroutine(Send(id.ToString("D"),callback));
        }
        IEnumerator Send(string orderId,Action<PrepaymentResult> callback){
            using(var request=new UnityWebRequest(endpoint+"/"+orderId+"/prepare","POST")){
                request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes("{}"));
                request.SetRequestHeader("Content-Type","application/json");request.SetRequestHeader("Authorization","Bearer "+session);
                var download=new BoundedJsonDownload();request.downloadHandler=download;request.timeout=15;request.redirectLimit=0;
                active.Add(request);yield return request.SendWebRequest();active.Remove(request);if(disposed)yield break;
                PrepaymentResult result;
                if(download.Exceeded)result=Fail("NETWORK.RESPONSE_TOO_LARGE","Prepayment response exceeds 65536 bytes; preserve order for reconciliation");
                else if(request.result==UnityWebRequest.Result.ConnectionError)result=Fail("PAY.PREPAYMENT_UNCERTAIN","Prepayment connection failed; preserve order for reconciliation");
                else {
                    PrepaymentResponseData data=null;try{data=JsonUtility.FromJson<PrepaymentResponseData>(download.ReadText());}catch{}
                    result=PrepaymentResponsePolicy.Evaluate(request.responseCode,data,orderId,request.GetResponseHeader("Retry-After"));
                }
                callback(result);
            }
        }
        static PrepaymentResult Fail(string code,string reason){return PrepaymentResult.Fail(new SdkError(code,reason));}
        public void Dispose(){if(disposed)return;disposed=true;session=null;foreach(var request in active)request.Abort();active.Clear();}
    }
}
#endif
