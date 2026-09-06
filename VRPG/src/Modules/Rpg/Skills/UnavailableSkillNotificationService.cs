using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace VRPG.Modules.Rpg.Skills;

// Profile-free fallback owned by one module/world, including when RPG itself is disabled.
internal sealed class UnavailableSkillNotificationService
{
    private const long MessageIntervalMs = 1600;
    private readonly Dictionary<string, long> nextMessageMs = new(StringComparer.OrdinalIgnoreCase);

    public void Notify(IServerPlayer player)
    {
        long now = player.Entity?.World?.ElapsedMilliseconds ?? Environment.TickCount64;
        if (nextMessageMs.TryGetValue(player.PlayerUID, out long next) && now < next) return;

        player.SendMessage(GlobalConstants.GeneralChatGroup, "Skills are unavailable right now.", EnumChatType.Notification);
        nextMessageMs[player.PlayerUID] = now + MessageIntervalMs;
    }

    public void RemovePlayer(string uid) => nextMessageMs.Remove(uid);
    public void Clear() => nextMessageMs.Clear();
}
