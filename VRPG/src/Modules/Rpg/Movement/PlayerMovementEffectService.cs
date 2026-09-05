using System;
using System.Collections.Generic;
using VRPG.Network;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace VRPG.Modules.Rpg.Movement;

public sealed class PlayerMovementEffectService
{
    public void RemovePlayer(string uid)
    {
        activeByPlayer.Remove(uid);
        sequenceByPlayer.Remove(uid);
    }
    public void Clear()
    {
        activeByPlayer.Clear();
        sequenceByPlayer.Clear();
    }
    private readonly ICoreServerAPI api;
    private readonly IServerNetworkChannel channel;
    private readonly Dictionary<string, long> sequenceByPlayer = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, ActiveEffect>> activeByPlayer = new(StringComparer.OrdinalIgnoreCase);

    public PlayerMovementEffectService(ICoreServerAPI api, IServerNetworkChannel channel)
    {
        this.api = api;
        this.channel = channel;
    }

    public void Apply(IServerPlayer player, MovementEffectRequest request)
    {
        if (player.Entity == null || string.IsNullOrWhiteSpace(request.EffectCode))
        {
            return;
        }

        MovementEffectPacket packet = CreatePacket(player.PlayerUID, request);
        if (activeByPlayer.TryGetValue(player.PlayerUID, out Dictionary<string, ActiveEffect>? existing))
        {
            existing.Remove(packet.EffectCode);
        }

        if (request.Mode == MovementEffectMode.BlendVelocity && packet.DurationMilliseconds > 0)
        {
            Dictionary<string, ActiveEffect> effects = GetEffects(player.PlayerUID);
            effects[packet.EffectCode] = new ActiveEffect(packet, api.World.ElapsedMilliseconds + packet.DurationMilliseconds);
        }
        else
        {
            MovementEffectEntityApplier.Apply(player.Entity, packet, 0f);
        }

        ApplyCollisionGrace(player.Entity, packet.CollisionGraceMilliseconds);
        channel.SendPacket(packet, player);
    }

    public void Cancel(IServerPlayer player, string effectCode)
    {
        if (string.IsNullOrWhiteSpace(effectCode))
        {
            return;
        }

        if (activeByPlayer.TryGetValue(player.PlayerUID, out Dictionary<string, ActiveEffect>? effects))
        {
            effects.Remove(effectCode);
        }

        channel.SendPacket(new MovementEffectPacket
        {
            Sequence = NextSequence(player.PlayerUID),
            EffectCode = effectCode,
            Mode = (byte)MovementEffectMode.Cancel
        }, player);
    }

    public void Tick(float deltaSeconds)
    {
        long now = api.World.ElapsedMilliseconds;
        foreach (IPlayer candidate in api.World.AllOnlinePlayers)
        {
            if (candidate is not IServerPlayer player
                || player.Entity == null
                || !activeByPlayer.TryGetValue(player.PlayerUID, out Dictionary<string, ActiveEffect>? effects))
            {
                continue;
            }

            ActiveEffect? selected = null;
            var expired = new List<string>();
            foreach (KeyValuePair<string, ActiveEffect> pair in effects)
            {
                if (pair.Value.EndMilliseconds <= now)
                {
                    expired.Add(pair.Key);
                    continue;
                }

                if (selected == null
                    || pair.Value.Packet.Priority > selected.Packet.Priority
                    || pair.Value.Packet.Priority == selected.Packet.Priority
                    && string.CompareOrdinal(pair.Key, selected.Packet.EffectCode) < 0)
                {
                    selected = pair.Value;
                }
            }

            foreach (string code in expired)
            {
                effects.Remove(code);
            }

            if (selected != null)
            {
                MovementEffectEntityApplier.Apply(player.Entity, selected.Packet, deltaSeconds);
            }
        }
    }

    private MovementEffectPacket CreatePacket(string playerUid, MovementEffectRequest request)
    {
        return new MovementEffectPacket
        {
            Sequence = NextSequence(playerUid),
            EffectCode = request.EffectCode,
            Mode = (byte)request.Mode,
            Axes = (byte)request.Axes,
            X = request.Vector.X,
            Y = request.Vector.Y,
            Z = request.Vector.Z,
            DurationMilliseconds = Math.Clamp(request.DurationMilliseconds, 0, 30000),
            ResponsePerSecond = Math.Clamp(request.ResponsePerSecond, 0.01f, 100f),
            Priority = Math.Clamp(request.Priority, -1000, 1000),
            CollisionGraceMilliseconds = Math.Clamp(request.CollisionGraceMilliseconds, 0, 2000)
        };
    }

    private long NextSequence(string playerUid)
    {
        long next = sequenceByPlayer.TryGetValue(playerUid, out long current) ? current + 1 : 1;
        sequenceByPlayer[playerUid] = next;
        return next;
    }

    private Dictionary<string, ActiveEffect> GetEffects(string playerUid)
    {
        if (!activeByPlayer.TryGetValue(playerUid, out Dictionary<string, ActiveEffect>? effects))
        {
            effects = new Dictionary<string, ActiveEffect>(StringComparer.OrdinalIgnoreCase);
            activeByPlayer[playerUid] = effects;
        }

        return effects;
    }

    private void ApplyCollisionGrace(EntityPlayer player, int milliseconds)
    {
        if (milliseconds <= 0)
        {
            return;
        }

        player.WatchedAttributes.SetLong(
            MovementEffectEntityApplier.CollisionGraceUntilAttribute,
            api.World.ElapsedMilliseconds + milliseconds);
        player.WatchedAttributes.MarkPathDirty(MovementEffectEntityApplier.CollisionGraceUntilAttribute);
    }

    private sealed record ActiveEffect(MovementEffectPacket Packet, long EndMilliseconds);
}
