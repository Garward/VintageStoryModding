using System;
using System.Collections.Generic;
using VRPG.Modules.Rpg.Movement;
using VRPG.Network;
using Vintagestory.API.Client;

namespace VRPG.Client.Movement;

public sealed class ClientMovementEffectController
{
    private readonly ICoreClientAPI api;
    private readonly Dictionary<string, ActiveEffect> active = new(StringComparer.OrdinalIgnoreCase);
    private long lastSequence;
    private long lastTickMilliseconds;

    public ClientMovementEffectController(ICoreClientAPI api)
    {
        this.api = api;
        lastTickMilliseconds = api.World.ElapsedMilliseconds;
    }

    public void Receive(MovementEffectPacket packet)
    {
        if (packet.ProtocolVersion != MovementEffectProtocol.CurrentVersion
            || packet.Sequence <= lastSequence
            || string.IsNullOrWhiteSpace(packet.EffectCode))
        {
            return;
        }

        lastSequence = packet.Sequence;
        MovementEffectMode mode = (MovementEffectMode)packet.Mode;
        if (mode == MovementEffectMode.Cancel)
        {
            active.Remove(packet.EffectCode);
            return;
        }

        if (mode == MovementEffectMode.BlendVelocity && packet.DurationMilliseconds > 0)
        {
            active[packet.EffectCode] = new ActiveEffect(
                packet,
                api.World.ElapsedMilliseconds + packet.DurationMilliseconds);
            return;
        }

        if (api.World.Player?.Entity != null)
        {
            MovementEffectEntityApplier.Apply(api.World.Player.Entity, packet, 0f);
        }
    }

    public void Tick()
    {
        long now = api.World.ElapsedMilliseconds;
        float deltaSeconds = Math.Clamp((now - lastTickMilliseconds) / 1000f, 0f, 0.25f);
        lastTickMilliseconds = now;
        if (api.World.Player?.Entity == null || active.Count == 0)
        {
            return;
        }

        ActiveEffect? selected = null;
        var expired = new List<string>();
        foreach (KeyValuePair<string, ActiveEffect> pair in active)
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
            active.Remove(code);
        }

        if (selected != null)
        {
            MovementEffectEntityApplier.Apply(api.World.Player.Entity, selected.Packet, deltaSeconds);
        }
    }

    public void Reset()
    {
        active.Clear();
        lastSequence = 0;
    }

    private sealed record ActiveEffect(MovementEffectPacket Packet, long EndMilliseconds);
}
