using VRPG.Network;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace VRPG.Modules.Rpg.Combat;

public sealed class CombatVisualBroadcaster
{
    public const float BroadcastRange = 80f;

    private readonly ICoreServerAPI api;
    private readonly IServerNetworkChannel channel;

    public CombatVisualBroadcaster(ICoreServerAPI api, IServerNetworkChannel channel)
    {
        this.api = api;
        this.channel = channel;
    }

    public void Send(CombatVisualEventPacket packet)
    {
        if (packet.ServerEventMs == 0)
        {
            packet.ServerEventMs = api.World.ElapsedMilliseconds;
        }

        double rangeSquared = BroadcastRange * BroadcastRange;
        foreach (IPlayer player in api.World.AllOnlinePlayers)
        {
            if (player is not IServerPlayer serverPlayer
                || serverPlayer.ConnectionState != EnumClientState.Playing
                || serverPlayer.Entity == null)
            {
                continue;
            }

            double dx = serverPlayer.Entity.Pos.X - packet.X;
            double dy = serverPlayer.Entity.Pos.Y - packet.Y;
            double dz = serverPlayer.Entity.Pos.Z - packet.Z;
            if (dx * dx + dy * dy + dz * dz <= rangeSquared)
            {
                channel.SendPacket(packet, serverPlayer);
            }
        }
    }
}
