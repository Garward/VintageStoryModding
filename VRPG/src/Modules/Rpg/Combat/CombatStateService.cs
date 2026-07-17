using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace VRPG.Modules.Rpg.Combat;

public sealed class CombatStateService
{
    private readonly ICoreServerAPI api;
    private readonly long lockDurationMs;
    private readonly Dictionary<string, long> combatUntilByPlayer = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

    public CombatStateService(ICoreServerAPI api, float lockSeconds)
    {
        this.api = api;
        lockDurationMs = (long)(Math.Max(1f, lockSeconds) * 1000f);
    }

    public void ObserveHostileDamage(Entity source, Entity target)
    {
        long combatUntil = api.World.ElapsedMilliseconds + lockDurationMs;
        MarkPlayer(source, combatUntil);
        MarkPlayer(target, combatUntil);
    }

    public bool IsInCombat(IServerPlayer player, out float remainingSeconds)
    {
        remainingSeconds = RemainingSeconds(player);
        return remainingSeconds > 0f;
    }

    public float RemainingSeconds(IServerPlayer player)
    {
        if (!combatUntilByPlayer.TryGetValue(player.PlayerUID, out long combatUntil))
        {
            return 0f;
        }

        long remainingMs = combatUntil - api.World.ElapsedMilliseconds;
        if (remainingMs <= 0)
        {
            combatUntilByPlayer.Remove(player.PlayerUID);
            return 0f;
        }

        return remainingMs / 1000f;
    }

    private void MarkPlayer(Entity entity, long combatUntil)
    {
        if (entity is not EntityPlayer player || string.IsNullOrWhiteSpace(player.PlayerUID))
        {
            return;
        }

        combatUntilByPlayer[player.PlayerUID] = combatUntil;
    }
}
