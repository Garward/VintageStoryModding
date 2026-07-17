using System;
using System.Collections.Generic;
using VRPG.Modules.Rpg.Players;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace VRPG.Modules.Rpg.Skills;

public sealed class AbilityFailureNotificationService
{
    private const long MessageIntervalMs = 1600;
    private const long SpamWindowMs = 6000;
    private const long SpamMuteMs = 8000;
    private const long ReminderIntervalMs = 30000;
    private const int SpamThreshold = 4;

    private readonly ICoreServerAPI api;
    private readonly RpgPlayerStore playerStore;
    private readonly Dictionary<string, RateLimitState> rateLimits = new Dictionary<string, RateLimitState>(StringComparer.OrdinalIgnoreCase);

    public AbilityFailureNotificationService(ICoreServerAPI api, RpgPlayerStore playerStore)
    {
        this.api = api;
        this.playerStore = playerStore;
    }

    public void Notify(IServerPlayer player, AbilityFailureKind kind, string message)
    {
        RpgPlayerState playerState = playerStore.GetOrCreate(player);
        if (!IsEnabled(playerState, kind))
        {
            return;
        }

        long now = api.World.ElapsedMilliseconds;
        string key = player.PlayerUID + ":" + kind;
        if (!rateLimits.TryGetValue(key, out RateLimitState? limit))
        {
            limit = new RateLimitState();
            rateLimits[key] = limit;
        }

        if (now < limit.MutedUntilMs)
        {
            return;
        }

        if (now >= limit.NextMessageMs)
        {
            Send(player, message);
            limit.NextMessageMs = now + MessageIntervalMs;
            return;
        }

        if (now - limit.SpamWindowStartedMs > SpamWindowMs)
        {
            limit.SpamWindowStartedMs = now;
            limit.SuppressedInWindow = 0;
        }

        limit.SuppressedInWindow++;
        if (limit.SuppressedInWindow < SpamThreshold)
        {
            return;
        }

        if (kind != AbilityFailureKind.Other && now - limit.LastReminderMs >= ReminderIntervalMs)
        {
            string label = kind == AbilityFailureKind.Cooldown ? "cooldown" : "resource";
            Send(player, $"Repeated {label} warnings suppressed. Disable {label} notifications in VRPG Hub -> Options.");
            limit.LastReminderMs = now;
        }

        limit.SuppressedInWindow = 0;
        limit.SpamWindowStartedMs = now;
        limit.MutedUntilMs = now + SpamMuteMs;
        limit.NextMessageMs = limit.MutedUntilMs;
    }

    private static bool IsEnabled(RpgPlayerState state, AbilityFailureKind kind)
    {
        return kind switch
        {
            AbilityFailureKind.Cooldown => state.ShowCooldownNotifications,
            AbilityFailureKind.InsufficientResource => state.ShowResourceNotifications,
            _ => true
        };
    }

    private static void Send(IServerPlayer player, string message)
    {
        player.SendMessage(
            GlobalConstants.GeneralChatGroup,
            string.IsNullOrWhiteSpace(message) ? "The VRPG ability could not be used." : message,
            EnumChatType.Notification);
    }

    private sealed class RateLimitState
    {
        public long NextMessageMs { get; set; }
        public long SpamWindowStartedMs { get; set; }
        public int SuppressedInWindow { get; set; }
        public long MutedUntilMs { get; set; }
        public long LastReminderMs { get; set; } = -ReminderIntervalMs;
    }
}
