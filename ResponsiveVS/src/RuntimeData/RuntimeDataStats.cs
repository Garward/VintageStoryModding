using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace ResponsiveVS.RuntimeData;

public static class RuntimeDataStats
{
    private static readonly object LockObj = new();
    private static readonly Dictionary<string, AsObjectStats> AsObjectByType = new();

    private static long asObjectHits;
    private static long asObjectMisses;
    private static long asObjectStores;
    private static long asObjectSkipped;
    private static long stackPackets;
    private static long stackAttributeBytes;
    private static int maxStackAttributeBytes;

    public static void RecordAsObjectHit(Type resultType)
    {
        lock (LockObj)
        {
            asObjectHits++;
            GetStats(resultType).Hits++;
        }
    }

    public static void RecordAsObjectMiss(Type resultType)
    {
        lock (LockObj)
        {
            asObjectMisses++;
            GetStats(resultType).Misses++;
        }
    }

    public static void RecordAsObjectStore(Type resultType, long elapsedTicks)
    {
        lock (LockObj)
        {
            AsObjectStats stats = GetStats(resultType);
            asObjectStores++;
            stats.Stores++;
            stats.TotalTicks += Math.Max(0, elapsedTicks);
            if (elapsedTicks > stats.MaxTicks)
            {
                stats.MaxTicks = elapsedTicks;
            }
        }
    }

    public static void RecordAsObjectSkipped(Type resultType)
    {
        lock (LockObj)
        {
            asObjectSkipped++;
            GetStats(resultType).Skipped++;
        }
    }

    public static void RecordStackPacket(int attributeBytes)
    {
        lock (LockObj)
        {
            stackPackets++;
            stackAttributeBytes += Math.Max(0, attributeBytes);
            if (attributeBytes > maxStackAttributeBytes)
            {
                maxStackAttributeBytes = attributeBytes;
            }
        }
    }

    public static void Reset()
    {
        lock (LockObj)
        {
            AsObjectByType.Clear();
            asObjectHits = 0;
            asObjectMisses = 0;
            asObjectStores = 0;
            asObjectSkipped = 0;
            stackPackets = 0;
            stackAttributeBytes = 0;
            maxStackAttributeBytes = 0;
        }
    }

    public static string Summary()
    {
        lock (LockObj)
        {
            long avgStackAttributeBytes = stackPackets == 0 ? 0 : stackAttributeBytes / stackPackets;
            StringBuilder sb = new();
            sb.Append(" RuntimeData: ");
            sb.Append("asObjectHits=").Append(asObjectHits);
            sb.Append(", asObjectMisses=").Append(asObjectMisses);
            sb.Append(", asObjectStores=").Append(asObjectStores);
            sb.Append(", asObjectSkipped=").Append(asObjectSkipped);
            sb.Append(", stackPackets=").Append(stackPackets);
            sb.Append(", avgStackAttrBytes=").Append(avgStackAttributeBytes);
            sb.Append(", maxStackAttrBytes=").Append(maxStackAttributeBytes);

            List<KeyValuePair<string, AsObjectStats>> top = AsObjectByType
                .OrderByDescending(entry => entry.Value.TotalCalls)
                .ThenByDescending(entry => entry.Value.TotalTicks)
                .Take(5)
                .ToList();

            if (top.Count > 0)
            {
                sb.Append(", topAsObject=[");
                for (int i = 0; i < top.Count; i++)
                {
                    if (i > 0) sb.Append("; ");
                    AsObjectStats stats = top[i].Value;
                    double totalMs = stats.TotalTicks * 1000.0 / Stopwatch.Frequency;
                    sb.Append(top[i].Key);
                    sb.Append(" calls=").Append(stats.TotalCalls);
                    sb.Append(" hits=").Append(stats.Hits);
                    sb.Append(" stores=").Append(stats.Stores);
                    sb.Append(" ms=").Append(totalMs.ToString("0.###"));
                }
                sb.Append(']');
            }

            return sb.ToString();
        }
    }

    private static AsObjectStats GetStats(Type resultType)
    {
        string key = resultType?.FullName ?? "<unknown>";
        if (!AsObjectByType.TryGetValue(key, out AsObjectStats stats))
        {
            stats = new AsObjectStats();
            AsObjectByType[key] = stats;
        }

        return stats;
    }

    private sealed class AsObjectStats
    {
        public long Hits;
        public long Misses;
        public long Stores;
        public long Skipped;
        public long TotalTicks;
        public long MaxTicks;

        public long TotalCalls => Hits + Misses + Skipped;
    }
}
