using System;
using System.Collections.Generic;
namespace ErDon.UnitySDK
{
    public sealed class NetworkRequest
    {
        readonly byte[] body;
        readonly Dictionary<string,string> headers;
        public string Url { get; private set; }
        public string Method { get; private set; }
        public int TimeoutSeconds { get; private set; }
        public int MaxResponseBytes { get; private set; }
        public byte[] Body { get { return (byte[])body.Clone(); } }
        public IDictionary<string,string> Headers { get { return new Dictionary<string,string>(headers,StringComparer.OrdinalIgnoreCase); } }
        public NetworkRequest(string url,string method="GET",byte[] body=null,IDictionary<string,string> headers=null,
            int timeoutSeconds=15,int maxResponseBytes=1048576)
        {
            Uri uri;
            if(!Uri.TryCreate(url,UriKind.Absolute,out uri)||uri.Scheme!="https"||!string.IsNullOrEmpty(uri.UserInfo)||!string.IsNullOrEmpty(uri.Fragment))
                throw new ArgumentException("Network URL must be HTTPS without credentials or fragment","url");
            method=(method??"").ToUpperInvariant();
            if(method!="GET"&&method!="POST"&&method!="PUT"&&method!="PATCH"&&method!="DELETE"&&method!="HEAD"&&method!="OPTIONS")
                throw new ArgumentException("Unsupported HTTP method","method");
            if(timeoutSeconds<1||timeoutSeconds>120)throw new ArgumentOutOfRangeException("timeoutSeconds");
            if(maxResponseBytes<1||maxResponseBytes>16777216)throw new ArgumentOutOfRangeException("maxResponseBytes");
            if(body!=null&&body.Length>1048576)throw new ArgumentException("Request body exceeds 1 MiB","body");
            if((method=="GET"||method=="HEAD")&&body!=null&&body.Length!=0)throw new ArgumentException("GET and HEAD cannot contain a body","body");
            this.headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            if(headers!=null)foreach(var header in headers){
                if(!IsHeaderName(header.Key)||header.Value==null||header.Value.IndexOf('\r')>=0||header.Value.IndexOf('\n')>=0||header.Value.IndexOf('\0')>=0)
                    throw new ArgumentException("Invalid HTTP header","headers");
                if(this.headers.ContainsKey(header.Key))throw new ArgumentException("Duplicate HTTP header","headers");
                this.headers.Add(header.Key,header.Value);
            }
            this.body=body==null?new byte[0]:(byte[])body.Clone();
            Url=uri.AbsoluteUri;Method=method;TimeoutSeconds=timeoutSeconds;MaxResponseBytes=maxResponseBytes;
        }
        static bool IsHeaderName(string name)
        {
            if(string.IsNullOrEmpty(name))return false;
            foreach(char c in name)if(!((c>='a'&&c<='z')||(c>='A'&&c<='Z')||(c>='0'&&c<='9')||"!#$%&'*+-.^_`|~".IndexOf(c)>=0))return false;
            return true;
        }
    }
    public sealed class NetworkResult
    {
        byte[] body=new byte[0];
        Dictionary<string,string> headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        public bool Success { get { return Error==null; } }
        // Like wx.request: HTTP 4xx/5xx is a completed response, not a connection failure.
        public bool IsHttpSuccess { get { return Success&&StatusCode>=200&&StatusCode<300; } }
        public long StatusCode { get; private set; }
        public byte[] Body { get { return (byte[])body.Clone(); } }
        public IDictionary<string,string> Headers { get { return new Dictionary<string,string>(headers,StringComparer.OrdinalIgnoreCase); } }
        public SdkError Error { get; private set; }
        public static NetworkResult Response(long status,byte[] body,IDictionary<string,string> headers)
        {
            if(status<100||status>599)throw new ArgumentOutOfRangeException("status");
            return new NetworkResult{StatusCode=status,body=body==null?new byte[0]:(byte[])body.Clone(),
                headers=headers==null?new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase):new Dictionary<string,string>(headers,StringComparer.OrdinalIgnoreCase)};
        }
        public static NetworkResult Fail(string code,string reason){return new NetworkResult{Error=new SdkError(code,reason)};}
    }
    public interface INetworkOperation { void Cancel(); }
    public interface INetwork : IDisposable
    {
        INetworkOperation Send(NetworkRequest request,Action<NetworkResult> callback);
    }
}
