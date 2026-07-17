using System.Collections.Generic;
using VRPG.Modules.Rpg.StatusEffects;
using Vintagestory.API.Datastructures;

namespace VRPG.Client.Visuals;

/// <summary>
/// Client-side view of an entity's synced statuses. Recomputes local end times
/// only when the synced revision changes; otherwise counts down locally.
/// </summary>
public sealed class EntityStatusCache
{
    private sealed class EntityEntry
    {
        public int Rev = -1;
        public long LastSeenMs;
        public readonly List<ActiveStatus> Statuses = new List<ActiveStatus>();
    }

    private readonly Dictionary<long, EntityEntry> byEntityId = new Dictionary<long, EntityEntry>();

    public IReadOnlyList<ActiveStatus> Update(long entityId, ITreeAttribute? entityAttributes, long nowMs)
    {
        if (!byEntityId.TryGetValue(entityId, out EntityEntry? entry))
        {
            entry = new EntityEntry();
            byEntityId[entityId] = entry;
        }

        entry.LastSeenMs = nowMs;
        int rev = entityAttributes?.GetTreeAttribute(StatusSync.TreeKey)?.GetInt("rev") ?? 0;
        if (rev != entry.Rev)
        {
            entry.Rev = rev;
            entry.Statuses.Clear();
            foreach (SyncedStatus status in StatusSync.Read(entityAttributes))
            {
                entry.Statuses.Add(new ActiveStatus
                {
                    Code = status.Code,
                    Stacks = status.Stacks,
                    Magnitude = status.Magnitude,
                    DurationMs = status.DurationMs,
                    EndMs = nowMs + status.RemainingMs
                });
            }
        }

        entry.Statuses.RemoveAll(status => status.EndMs <= nowMs);
        return entry.Statuses;
    }

    public void Prune(long nowMs)
    {
        var stale = new List<long>();
        foreach (KeyValuePair<long, EntityEntry> pair in byEntityId)
        {
            if (nowMs - pair.Value.LastSeenMs > 10000)
            {
                stale.Add(pair.Key);
            }
        }

        for (int i = 0; i < stale.Count; i++)
        {
            byEntityId.Remove(stale[i]);
        }
    }
}

public sealed class ActiveStatus
{
    public string Code = "";
    public int Stacks;
    public float Magnitude;
    public long EndMs;
    public int DurationMs;

    public float RemainingSeconds(long nowMs)
    {
        return System.Math.Max(0f, (EndMs - nowMs) / 1000f);
    }
}
