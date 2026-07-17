using System;
using System.Collections.Generic;
using VRPG.Network;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VRPG.Modules.Rpg.Combat;

public sealed class GroundAreaRecord
{
    public long Id;
    public string OwnerUid = "";
    public string StyleCode = "";
    public GroundAreaShape Shape;
    public double X, Y, Z;
    public long FollowEntityId;
    public float Radius;
    public GroundAreaState State;
    public long ExpiresAtMs;
}

public sealed class ExpiryTransitions
{
    public readonly List<GroundAreaRecord> NowExpiring = new List<GroundAreaRecord>();
    public readonly List<long> Expired = new List<long>();
}

/// <summary>Pure area bookkeeping so expiry rules are unit-testable.</summary>
public sealed class GroundAreaTable
{
    public const int ExpiringLeadMs = 1500;

    private readonly Dictionary<long, GroundAreaRecord> areas = new Dictionary<long, GroundAreaRecord>();

    public IReadOnlyCollection<GroundAreaRecord> All => areas.Values;

    public void Upsert(GroundAreaRecord record)
    {
        areas[record.Id] = record;
    }

    public GroundAreaRecord? Get(long id)
    {
        return areas.TryGetValue(id, out GroundAreaRecord? record) ? record : null;
    }

    public bool Remove(long id)
    {
        return areas.Remove(id);
    }

    public ExpiryTransitions TickExpiry(long nowMs)
    {
        var transitions = new ExpiryTransitions();
        foreach (GroundAreaRecord record in areas.Values)
        {
            if (record.ExpiresAtMs <= nowMs)
            {
                transitions.Expired.Add(record.Id);
            }
            else if (record.State != GroundAreaState.Expiring && record.ExpiresAtMs - nowMs <= ExpiringLeadMs)
            {
                record.State = GroundAreaState.Expiring;
                transitions.NowExpiring.Add(record);
            }
        }

        for (int i = 0; i < transitions.Expired.Count; i++)
        {
            areas.Remove(transitions.Expired[i]);
        }

        return transitions;
    }
}

public sealed class GroundAreaService : IDisposable
{
    private readonly ICoreServerAPI api;
    private readonly IServerNetworkChannel channel;
    private readonly GroundAreaTable table = new GroundAreaTable();
    private readonly long tickListenerId;
    private long nextId = 1;

    public GroundAreaService(ICoreServerAPI api, IServerNetworkChannel channel)
    {
        this.api = api;
        this.channel = channel;
        tickListenerId = api.Event.RegisterGameTickListener(Tick, 250);
    }

    public long Place(string ownerUid, string styleCode, GroundAreaShape shape, Vec3d center, float radius, GroundAreaState state, float durationSeconds, long followEntityId = 0)
    {
        var record = new GroundAreaRecord
        {
            Id = nextId++,
            OwnerUid = ownerUid,
            StyleCode = styleCode,
            Shape = shape,
            X = center.X,
            Y = center.Y,
            Z = center.Z,
            FollowEntityId = followEntityId,
            Radius = radius,
            State = state,
            ExpiresAtMs = api.World.ElapsedMilliseconds + (long)(durationSeconds * 1000f)
        };
        table.Upsert(record);
        Broadcast(record);
        return record.Id;
    }

    public void SetState(long id, GroundAreaState state)
    {
        GroundAreaRecord? record = table.Get(id);
        if (record == null)
        {
            return;
        }

        record.State = state;
        Broadcast(record);
    }

    public void Remove(long id)
    {
        if (table.Remove(id))
        {
            BroadcastToAll(new GroundAreaRemovePacket { Id = id });
        }
    }

    public void SendSnapshot(IServerPlayer player)
    {
        foreach (GroundAreaRecord record in table.All)
        {
            channel.SendPacket(ToPacket(record), player);
        }
    }

    private void Tick(float dt)
    {
        ExpiryTransitions transitions = table.TickExpiry(api.World.ElapsedMilliseconds);
        for (int i = 0; i < transitions.NowExpiring.Count; i++)
        {
            Broadcast(transitions.NowExpiring[i]);
        }

        for (int i = 0; i < transitions.Expired.Count; i++)
        {
            BroadcastToAll(new GroundAreaRemovePacket { Id = transitions.Expired[i] });
        }
    }

    private GroundAreaUpsertPacket ToPacket(GroundAreaRecord record)
    {
        return new GroundAreaUpsertPacket
        {
            Id = record.Id,
            OwnerUid = record.OwnerUid,
            StyleCode = record.StyleCode,
            Shape = (byte)record.Shape,
            X = record.X,
            Y = record.Y,
            Z = record.Z,
            FollowEntityId = record.FollowEntityId,
            Radius = record.Radius,
            State = (byte)record.State,
            RemainingMs = (int)Math.Max(0, record.ExpiresAtMs - api.World.ElapsedMilliseconds)
        };
    }

    // Areas are few (wards, traps); broadcast to everyone and let the client range-filter.
    private void Broadcast(GroundAreaRecord record)
    {
        BroadcastToAll(ToPacket(record));
    }

    private void BroadcastToAll(object packet)
    {
        foreach (IPlayer player in api.World.AllOnlinePlayers)
        {
            if (player is IServerPlayer serverPlayer && serverPlayer.ConnectionState == EnumClientState.Playing)
            {
                if (packet is GroundAreaUpsertPacket upsert)
                {
                    channel.SendPacket(upsert, serverPlayer);
                }
                else if (packet is GroundAreaRemovePacket remove)
                {
                    channel.SendPacket(remove, serverPlayer);
                }
            }
        }
    }

    public void Dispose()
    {
        api.Event.UnregisterGameTickListener(tickListenerId);
    }
}
