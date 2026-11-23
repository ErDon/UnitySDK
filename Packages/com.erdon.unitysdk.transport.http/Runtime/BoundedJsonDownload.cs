using System;
using System.Text;
using UnityEngine.Networking;

namespace ErDon.UnitySDK
{
    // Shared by authentication and orders even when the optional network module is excluded.
    internal sealed class BoundedJsonDownload : DownloadHandlerScript
    {
        internal const int Limit = 65536;
        readonly byte[] buffer = new byte[Limit];
        int count;
        internal bool Exceeded { get; private set; }
        internal BoundedJsonDownload() : base(new byte[8192]) { }
        protected override bool ReceiveData(byte[] data, int length)
        {
            if (Exceeded || data == null || length < 0 || length > data.Length) return false;
            if (length > Limit - count) { Exceeded = true; return false; }
            Buffer.BlockCopy(data, 0, buffer, count, length);
            count += length;
            return true;
        }
        internal string ReadText() { return Encoding.UTF8.GetString(buffer, 0, count); }
    }
}
