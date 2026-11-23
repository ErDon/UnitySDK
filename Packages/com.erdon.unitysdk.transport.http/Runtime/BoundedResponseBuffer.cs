#if !ERDON_EXCLUDE_NETWORK || UNITY_EDITOR
using System;
using System.IO;
namespace ErDon.UnitySDK
{
    // Enforces the limit while receiving, before retaining the next chunk.
    public sealed class BoundedResponseBuffer : IDisposable
    {
        readonly int limit;
        readonly MemoryStream stream=new MemoryStream();
        public bool Exceeded {get;private set;}
        public BoundedResponseBuffer(int limit){if(limit<1)throw new ArgumentOutOfRangeException("limit");this.limit=limit;}
        public bool Append(byte[] data,int length)
        {
            if(data==null||length<0||length>data.Length)throw new ArgumentException("Invalid response chunk");
            if(Exceeded)return false;
            if(length>limit-stream.Length){Exceeded=true;return false;}
            stream.Write(data,0,length);return true;
        }
        public byte[] ToArray(){return stream.ToArray();}
        public void Dispose(){stream.Dispose();}
    }
}

#endif
