using VRPG.Network;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace VRPG.Modules.Rpg.Movement;

public static class MovementEffectEntityApplier
{
    public const string CollisionGraceUntilAttribute = "vrpgMovementCollisionGraceUntilMs";

    public static void Apply(EntityPlayer player, MovementEffectPacket packet, float deltaSeconds)
    {
        MovementVector current = new(player.Pos.Motion.X, player.Pos.Motion.Y, player.Pos.Motion.Z);
        MovementVector requested = new(packet.X, packet.Y, packet.Z);
        MovementVector resolved = MovementEffectMath.Apply(
            current,
            requested,
            (MovementEffectMode)packet.Mode,
            (MovementEffectAxes)packet.Axes,
            packet.ResponsePerSecond,
            deltaSeconds);

        player.Pos.Motion.Set(resolved.X, resolved.Y, resolved.Z);
        player.WatchedAttributes.SetDouble("kbdirX", resolved.X);
        player.WatchedAttributes.SetDouble("kbdirY", resolved.Y);
        player.WatchedAttributes.SetDouble("kbdirZ", resolved.Z);
        player.Attributes.SetInt("dmgkb", 1);
    }

    public static bool HasCollisionGrace(EntityPlayer player, long elapsedMilliseconds)
    {
        return player.WatchedAttributes.GetLong(CollisionGraceUntilAttribute) > elapsedMilliseconds;
    }
}
