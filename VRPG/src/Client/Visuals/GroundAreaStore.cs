using System.Collections.Generic;
using VRPG.Network;

namespace VRPG.Client.Visuals;

public sealed class ClientGroundArea
{
    public long Id;
    public string OwnerUid = "";
    public string StyleCode = "";
    public GroundAreaShape Shape;
    public double X, Y, Z;
    public long FollowEntityId;
    public float Radius;
    public GroundAreaState State;
    public long LocalExpiresAtMs;
    public long StateChangedAtMs;
}

public sealed class GroundAreaStore
{
    private readonly Dictionary<long, ClientGroundArea> areas = new Dictionary<long, ClientGroundArea>();

    public IReadOnlyCollection<ClientGroundArea> All => areas.Values;

    public void Upsert(GroundAreaUpsertPacket packet, long nowMs)
    {
        if (!areas.TryGetValue(packet.Id, out ClientGroundArea? area))
        {
            area = new ClientGroundArea { Id = packet.Id, StateChangedAtMs = nowMs };
            areas[packet.Id] = area;
        }
        else if (area.State != (GroundAreaState)packet.State)
        {
            area.StateChangedAtMs = nowMs;
        }

        area.OwnerUid = packet.OwnerUid;
        area.StyleCode = packet.StyleCode;
        area.Shape = (GroundAreaShape)packet.Shape;
        area.X = packet.X;
        area.Y = packet.Y;
        area.Z = packet.Z;
        area.FollowEntityId = packet.FollowEntityId;
        area.Radius = packet.Radius;
        area.State = (GroundAreaState)packet.State;
        area.LocalExpiresAtMs = nowMs + packet.RemainingMs;
    }

    public void Remove(long id)
    {
        areas.Remove(id);
    }

    public void Prune(long nowMs)
    {
        var expired = new List<long>();
        foreach (ClientGroundArea area in areas.Values)
        {
            if (area.LocalExpiresAtMs <= nowMs)
            {
                expired.Add(area.Id);
            }
        }

        for (int i = 0; i < expired.Count; i++)
        {
            areas.Remove(expired[i]);
        }
    }
}
