using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
namespace ErDon.UnitySDK
{
    public sealed class UnitySessionExchange : ISessionExchange
    {
        [Serializable] sealed class RequestData { public string gameId; public string appId; public string environment; public string platform; public string code; }
        readonly MonoBehaviour host;
        readonly string endpoint, gameId, appId, environment;
        readonly HashSet<UnityWebRequest> active = new HashSet<UnityWebRequest>();
        bool disposed;
        public UnitySessionExchange(MonoBehaviour host, string endpoint, string gameId, string appId, string environment)
        {
            Uri uri;
            if(host==null)throw new ArgumentNullException("host");
            if(!Uri.TryCreate(endpoint,UriKind.Absolute,out uri) || uri.Scheme!="https" || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
                throw new ArgumentException("Session endpoint must be HTTPS without credentials or fragment");
            if(string.IsNullOrWhiteSpace(gameId)||string.IsNullOrWhiteSpace(appId)||string.IsNullOrWhiteSpace(environment))throw new ArgumentException("Missing game/app/environment");
            this.host=host;this.endpoint=endpoint;this.gameId=gameId;this.appId=appId;this.environment=environment;
        }
        public void Exchange(PlatformCredential credential, Action<LoginResult> callback)
        {
            if(callback==null)throw new ArgumentNullException("callback");
            if(disposed){callback(LoginResult.Fail("SDK.DISPOSED","Exchange disposed"));return;}
            if(credential==null || string.IsNullOrWhiteSpace(credential.Code) || string.IsNullOrWhiteSpace(credential.Platform)) {
                callback(LoginResult.Fail("AUTH.INVALID_CREDENTIAL","Session exchange requires a platform credential"));return;
            }
            if(host==null || !host.isActiveAndEnabled) {
                callback(LoginResult.Fail("NETWORK.HOST_UNAVAILABLE","Session exchange host is inactive or destroyed"));return;
            }
            host.StartCoroutine(Send(credential,callback));
        }
        IEnumerator Send(PlatformCredential credential, Action<LoginResult> callback)
        {
            var body=JsonUtility.ToJson(new RequestData{gameId=gameId,appId=appId,environment=environment,platform=credential.Platform,code=credential.Code});
            using(var request=new UnityWebRequest(endpoint,"POST")) {
                request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                var download = new BoundedJsonDownload();
                request.downloadHandler=download;
                request.SetRequestHeader("Content-Type","application/json");
                request.timeout=15;request.redirectLimit=0;
                active.Add(request);
                yield return request.SendWebRequest();
                active.Remove(request);
                if(disposed)yield break;
                LoginResult result;
                if(download.Exceeded)
                    result=LoginResult.Fail("NETWORK.RESPONSE_TOO_LARGE","Session response exceeds 65536 bytes");
                else if(request.result==UnityWebRequest.Result.ConnectionError)
                    result=LoginResult.Fail("NETWORK.UNAVAILABLE","Session endpoint connection failed");
                else {
                    try {
                        var response=JsonUtility.FromJson<SessionResponseData>(download.ReadText());
                        result=SessionResponsePolicy.Evaluate(request.responseCode,response,request.GetResponseHeader("Retry-After"));
                    }catch{
                        result=request.responseCode==200
                            ? LoginResult.Fail("AUTH.INVALID_RESPONSE","Malformed session response")
                            : SessionResponsePolicy.Evaluate(request.responseCode,null,request.GetResponseHeader("Retry-After"));
                    }
                }
                callback(result);
            }
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            foreach(var request in active)request.Abort();
            active.Clear();
        }
    }
}
