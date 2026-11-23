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
    public sealed class UnityOrderClient : IOrderClient
    {
        [Serializable] sealed class CreateBody { public string productId,requestId; }
        [Serializable] sealed class ConfirmBody { public string evidence; }
        readonly MonoBehaviour host;readonly string endpoint;
        readonly HashSet<UnityWebRequest> active=new HashSet<UnityWebRequest>();bool disposed;
        public UnityOrderClient(MonoBehaviour host,string ordersEndpoint)
        {
            Uri uri;if(host==null)throw new ArgumentNullException("host");
            if(!Uri.TryCreate(ordersEndpoint,UriKind.Absolute,out uri)||uri.Scheme!="https"||!string.IsNullOrEmpty(uri.UserInfo)||
                !string.IsNullOrEmpty(uri.Query)||!string.IsNullOrEmpty(uri.Fragment))throw new ArgumentException("Orders endpoint must be HTTPS without query, credentials or fragment");
            this.host=host;endpoint=uri.AbsoluteUri.TrimEnd('/');
        }
        public void Create(string session,string productId,string requestId,Action<OrderResult> callback)
        {
            if(!Validate(session,callback))return;
            if(string.IsNullOrWhiteSpace(productId)||string.IsNullOrWhiteSpace(requestId)||productId.Length>256||requestId.Length>256) {
                callback(OrderResult.Fail("PAY.INVALID_REQUEST","Product and idempotency request identifiers are required"));return;
            }
            Start(session,"POST",endpoint,JsonUtility.ToJson(new CreateBody{productId=productId,requestId=requestId}),null,productId,callback);
        }
        public void Get(string session,string orderId,Action<OrderResult> callback) { RequestOrder(session,orderId,null,false,callback); }
        public void Confirm(string session,string orderId,string evidence,Action<OrderResult> callback) { RequestOrder(session,orderId,evidence,true,callback); }
        void RequestOrder(string session,string orderId,string evidence,bool confirm,Action<OrderResult> callback)
        {
            if(!Validate(session,callback))return;Guid id;
            if(!Guid.TryParseExact(orderId,"D",out id)){callback(OrderResult.Fail("PAY.INVALID_REQUEST","Invalid order identifier"));return;}
            var canonical=id.ToString("D");
            Start(session,confirm?"POST":"GET",endpoint+"/"+canonical+(confirm?"/confirm":""),
                confirm?JsonUtility.ToJson(new ConfirmBody{evidence=evidence}):null,canonical,null,callback);
        }
        bool Validate(string session,Action<OrderResult> callback)
        {
            if(callback==null)throw new ArgumentNullException("callback");
            if(disposed){callback(OrderResult.Fail("SDK.DISPOSED","Order client was disposed"));return false;}
            if(session==null||!Regex.IsMatch(session,"\\A[A-Za-z0-9_-]{43}\\z")){callback(OrderResult.Fail("AUTH.SESSION_INVALID","Invalid game session"));return false;}
            return true;
        }
        void Start(string session,string method,string url,string body,string orderId,string productId,Action<OrderResult> callback)
        {
            if(body!=null&&Encoding.UTF8.GetByteCount(body)>8192){callback(OrderResult.Fail("REQUEST.TOO_LARGE","Order request exceeds size limit"));return;}
            host.StartCoroutine(Send(session,method,url,body,orderId,productId,callback));
        }
        IEnumerator Send(string session,string method,string url,string body,string orderId,string productId,Action<OrderResult> callback)
        {
            using(var request=new UnityWebRequest(url,method)) {
                if(body!=null){request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));request.SetRequestHeader("Content-Type","application/json");}
                var download = new BoundedJsonDownload();
                request.SetRequestHeader("Authorization","Bearer "+session);request.downloadHandler=download;request.timeout=15;request.redirectLimit=0;
                active.Add(request);yield return request.SendWebRequest();active.Remove(request);if(disposed)yield break;
                OrderResult result;
                if(download.Exceeded)result=OrderResult.Fail("NETWORK.RESPONSE_TOO_LARGE","Order response exceeds 65536 bytes");
                else if(request.result==UnityWebRequest.Result.ConnectionError)result=OrderResult.Fail("NETWORK.UNAVAILABLE","Order service connection failed");
                else {
                    OrderResponseData data=null;
                    try { data=JsonUtility.FromJson<OrderResponseData>(download.ReadText()); }catch { }
                    result=OrderResponsePolicy.Evaluate(request.responseCode,data,orderId,productId,request.GetResponseHeader("Retry-After"));
                }
                callback(result);
            }
        }
        public void Dispose(){if(disposed)return;disposed=true;foreach(var request in active)request.Abort();active.Clear();}
    }
}

#endif
