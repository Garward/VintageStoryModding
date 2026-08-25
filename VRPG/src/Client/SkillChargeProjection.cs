using System;
using VRPG.Network;

namespace VRPG.Client;

/// <summary>
/// Projects sequential charge recovery between server loadout snapshots. Shared
/// by input and HUD code so a displayed charge is always considered castable.
/// </summary>
public static class SkillChargeProjection
{
    private const double TimerToleranceSeconds = 0.0005;

    public static int Current(
        SkillLoadoutSlotPacket entry,
        long snapshotAtMilliseconds,
        long nowMilliseconds)
    {
        int maximum = Math.Max(1, entry.MaximumCharges);
        int current = Math.Clamp(entry.CurrentCharges, 0, maximum);
        if (maximum <= 1 || current >= maximum || entry.CooldownSeconds <= 0f)
        {
            return current;
        }

        double firstRecovery = Math.Max(0.0, entry.CooldownRemainingSeconds);
        if (firstRecovery <= 0.0)
        {
            return current;
        }

        double elapsed = ElapsedSeconds(snapshotAtMilliseconds, nowMilliseconds);
        if (elapsed + TimerToleranceSeconds < firstRecovery)
        {
            return current;
        }

        double afterFirstRecovery = Math.Max(0.0, elapsed - firstRecovery);
        int recovered = 1 + (int)Math.Floor(afterFirstRecovery / entry.CooldownSeconds);
        return Math.Min(maximum, current + recovered);
    }

    public static double RemainingSeconds(
        SkillLoadoutSlotPacket entry,
        long snapshotAtMilliseconds,
        long nowMilliseconds)
    {
        double elapsed = ElapsedSeconds(snapshotAtMilliseconds, nowMilliseconds);
        double firstRecovery = Math.Max(0.0, entry.CooldownRemainingSeconds);
        if (entry.MaximumCharges <= 1)
        {
            return Math.Max(0.0, firstRecovery - elapsed);
        }

        int current = Math.Clamp(entry.CurrentCharges, 0, Math.Max(1, entry.MaximumCharges));
        if (current >= entry.MaximumCharges
            || entry.CooldownSeconds <= 0f
            || firstRecovery <= 0.0)
        {
            return 0.0;
        }

        if (elapsed + TimerToleranceSeconds < firstRecovery)
        {
            return firstRecovery - elapsed;
        }

        double afterFirstRecovery = Math.Max(0.0, elapsed - firstRecovery);
        int recovered = 1 + (int)Math.Floor(afterFirstRecovery / entry.CooldownSeconds);
        if (current + recovered >= entry.MaximumCharges)
        {
            return 0.0;
        }

        double phase = afterFirstRecovery % entry.CooldownSeconds;
        return entry.CooldownSeconds - phase;
    }

    private static double ElapsedSeconds(long snapshotAtMilliseconds, long nowMilliseconds)
    {
        return Math.Max(0.0, (nowMilliseconds - snapshotAtMilliseconds) / 1000.0);
    }
}
