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
        [Serializable] sealed class ResponseData { public string session; public string errorCode; }
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
            if(disposed){callback(LoginResult.Fail("SDK.DISPOSED","Exchange disposed"));return;}
            host.StartCoroutine(Send(credential,callback));
        }
        IEnumerator Send(PlatformCredential credential, Action<LoginResult> callback)
        {
            var body=JsonUtility.ToJson(new RequestData{gameId=gameId,appId=appId,environment=environment,platform=credential.Platform,code=credential.Code});
            using(var request=new UnityWebRequest(endpoint,"POST")) {
                request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                request.downloadHandler=new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type","application/json");
                request.timeout=15;request.redirectLimit=0;
                active.Add(request);
                yield return request.SendWebRequest();
                active.Remove(request);
                if(disposed)yield break;
                LoginResult result;
                if(request.result==UnityWebRequest.Result.ConnectionError)
                    result=LoginResult.Fail("NETWORK.UNAVAILABLE","Session endpoint connection failed");
                else if(request.responseCode==429)
                    result=LoginResult.Fail(new SdkError("AUTH.RATE_LIMITED","Session endpoint rate limited").WithRetry(RetryPolicy.Delayed));
                else if(request.responseCode!=200)
                    result=LoginResult.Fail("AUTH.SERVER_REJECTED","Session endpoint rejected request");
                else {
                    try {
                        if(request.downloadHandler.data.Length>65536)throw new FormatException();
                        var response=JsonUtility.FromJson<ResponseData>(request.downloadHandler.text);
                        if(response==null || !string.IsNullOrEmpty(response.errorCode) || string.IsNullOrWhiteSpace(response.session))
                            result=LoginResult.Fail("AUTH.INVALID_SESSION","Session response is invalid");
                        else result=LoginResult.Ok(response.session);
                    }catch{result=LoginResult.Fail("AUTH.INVALID_RESPONSE","Malformed session response");}
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
