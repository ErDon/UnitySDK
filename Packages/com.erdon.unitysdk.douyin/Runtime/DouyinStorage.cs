using System;
using TTSDK;

namespace ErDon.UnitySDK.Douyin
{
    /// <summary>String storage isolated by game/environment. Initialize the official runtime first.</summary>
    public sealed class DouyinStorage : IStorage
    {
        const string ValuePrefix = "erdon-string-v1:";
        readonly string prefix;
        readonly Action<string, string> set;
        readonly Func<string, string, string> get;
        readonly Func<string, bool> has;
        readonly Action<string> remove;
        bool disposed;

        public DouyinStorage(string gameId, string environment)
            : this(gameId, environment, SetAndSave, (key, fallback) => TT.PlayerPrefs.GetString(key, fallback), key => TT.PlayerPrefs.HasKey(key), RemoveAndSave) { }

        static void SetAndSave(string key, string value) {
            TT.PlayerPrefs.SetString(key, value);
            TT.PlayerPrefs.Save();
        }
        static void RemoveAndSave(string key) {
            TT.PlayerPrefs.DeleteKey(key);
            TT.PlayerPrefs.Save();
        }

        internal DouyinStorage(string gameId, string environment, Action<string, string> set,
            Func<string, string, string> get, Func<string, bool> has, Action<string> remove)
        {
            if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(environment)) throw new ArgumentException("Game and environment are required");
            if (set == null || get == null || has == null || remove == null) throw new ArgumentNullException();
            prefix = "erdon:v1:" + Uri.EscapeDataString(gameId) + ":" + Uri.EscapeDataString(environment) + ":";
            this.set=set; this.get=get; this.has=has; this.remove=remove;
        }
        StorageResult Validate(string key) {
            if (disposed) return StorageResult.Fail("SDK.DISPOSED", "Douyin storage was disposed");
            if (string.IsNullOrWhiteSpace(key)) return StorageResult.Fail("STORAGE.INVALID_ARGUMENT", "Storage key must not be blank");
            return null;
        }
        public StorageResult Set(string key, string value)
        {
            var invalid=Validate(key); if(invalid!=null)return invalid;
            if(value==null)return StorageResult.Fail("STORAGE.INVALID_ARGUMENT", "Storage value must not be null");
            try {
                string physical=prefix+key, encoded=ValuePrefix+value;
                set(physical,encoded);
                // Verify the observed value; this does not prove durability after a device restart.
                return has(physical) && get(physical,null)==encoded ? StorageResult.Ok()
                    : StorageResult.Fail("STORAGE.WRITE_FAILED", "Douyin did not retain the requested value");
            } catch { return StorageResult.Fail("STORAGE.PROVIDER_FAILED", "Douyin storage write failed"); }
        }
        public StorageResult Get(string key)
        {
            var invalid=Validate(key); if(invalid!=null)return invalid;
            try {
                string physical=prefix+key;
                if(!has(physical))return StorageResult.Fail("STORAGE.NOT_FOUND", "Storage key does not exist");
                string encoded=get(physical,null);
                if(encoded==null)return StorageResult.Fail("STORAGE.READ_FAILED", "Douyin returned no value for an existing key");
                if(!encoded.StartsWith(ValuePrefix,StringComparison.Ordinal))return StorageResult.Fail("STORAGE.FORMAT_MISMATCH", "Stored value uses an unsupported format");
                return StorageResult.Ok(encoded.Substring(ValuePrefix.Length));
            } catch { return StorageResult.Fail("STORAGE.PROVIDER_FAILED", "Douyin storage read failed"); }
        }
        public StorageResult Remove(string key)
        {
            var invalid=Validate(key); if(invalid!=null)return invalid;
            try {
                string physical=prefix+key;
                if(has(physical))remove(physical);
                return !has(physical) ? StorageResult.Ok() : StorageResult.Fail("STORAGE.REMOVE_FAILED", "Douyin retained the key after removal");
            } catch { return StorageResult.Fail("STORAGE.PROVIDER_FAILED", "Douyin storage removal failed"); }
        }
        public void Dispose() { disposed=true; } // Do not erase persisted player data.
    }
}

