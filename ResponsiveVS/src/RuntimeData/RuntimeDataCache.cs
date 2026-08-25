using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using ResponsiveVS.Config;

namespace ResponsiveVS.RuntimeData;

public static class RuntimeDataCache
{
    private static readonly object LockObj = new();
    private static readonly Dictionary<AsObjectCacheKey, object> AsObjectCache = new();
    private static readonly object NullValue = new();

    public static bool Enabled => ResponsiveVSConfigSystem.Config.RuntimeData.EnableRuntimeDataHotPathPatch;

    public static bool AsObjectCacheEnabled
    {
        get
        {
            RuntimeDataConfig config = ResponsiveVSConfigSystem.Config.RuntimeData;
            return config.EnableRuntimeDataHotPathPatch
                && config.EnableAsObjectResultCache
                && config.MaxCachedAsObjectResults > 0;
        }
    }

    public static void Clear()
    {
        lock (LockObj)
        {
            AsObjectCache.Clear();
        }
    }

    public static bool TryGetAsObject<T>(JToken token, string domain, out T value)
    {
        value = default;
        if (!AsObjectCacheEnabled || token == null)
        {
            return false;
        }

        AsObjectCacheKey key = new(token, typeof(T), domain);
        lock (LockObj)
        {
            if (!AsObjectCache.TryGetValue(key, out object cached))
            {
                return false;
            }

            value = ReferenceEquals(cached, NullValue) ? default : (T)cached;
            return true;
        }
    }

    public static void StoreAsObject<T>(JToken token, string domain, T value)
    {
        RuntimeDataConfig config = ResponsiveVSConfigSystem.Config.RuntimeData;
        if (!AsObjectCacheEnabled || token == null)
        {
            return;
        }

        AsObjectCacheKey key = new(token, typeof(T), domain);
        object cached = value == null ? NullValue : value;

        lock (LockObj)
        {
            if (AsObjectCache.Count >= config.MaxCachedAsObjectResults && !AsObjectCache.ContainsKey(key))
            {
                return;
            }

            AsObjectCache[key] = cached;
        }
    }

    private sealed class AsObjectCacheKey : IEquatable<AsObjectCacheKey>
    {
        private readonly JToken token;
        private readonly Type resultType;
        private readonly string domain;
        private readonly int hashCode;

        public AsObjectCacheKey(JToken token, Type resultType, string domain)
        {
            this.token = token;
            this.resultType = resultType;
            this.domain = domain ?? string.Empty;
            hashCode = HashCode.Combine(RuntimeHelpers.GetHashCode(token), resultType, this.domain);
        }

        public bool Equals(AsObjectCacheKey other)
        {
            return other != null
                && ReferenceEquals(token, other.token)
                && resultType == other.resultType
                && string.Equals(domain, other.domain, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as AsObjectCacheKey);
        }

        public override int GetHashCode()
        {
            return hashCode;
        }
    }
}
