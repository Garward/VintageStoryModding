using System;
using System.Collections.Generic;

namespace VRPG.Modules.Rpg.Skills;

public readonly record struct SkillChargeSnapshot(
    int Current,
    int Maximum,
    long NextRechargeAtMilliseconds)
{
    public float RechargeRemainingSeconds(long nowMilliseconds)
    {
        return NextRechargeAtMilliseconds <= 0
            ? 0f
            : Math.Max(0f, (NextRechargeAtMilliseconds - nowMilliseconds) / 1000f);
    }
}

/// <summary>Tracks server-authoritative, sequentially recovering skill charges.</summary>
public sealed class SkillChargeTracker
{
    private readonly Dictionary<string, Dictionary<string, ChargeState>> states =
        new Dictionary<string, Dictionary<string, ChargeState>>(StringComparer.OrdinalIgnoreCase);

    public SkillChargeSnapshot Snapshot(
        string playerUid,
        string skillCode,
        int maximum,
        long rechargeMilliseconds,
        long nowMilliseconds)
    {
        ChargeState state = GetOrCreate(playerUid, skillCode, maximum);
        Refresh(state, maximum, rechargeMilliseconds, nowMilliseconds);
        return ToSnapshot(state, maximum);
    }

    public bool TryConsume(
        string playerUid,
        string skillCode,
        int maximum,
        long rechargeMilliseconds,
        long nowMilliseconds,
        out SkillChargeSnapshot snapshot)
    {
        ChargeState state = GetOrCreate(playerUid, skillCode, maximum);
        Refresh(state, maximum, rechargeMilliseconds, nowMilliseconds);
        if (state.Current <= 0)
        {
            snapshot = ToSnapshot(state, maximum);
            return false;
        }

        state.Current--;
        if (state.NextRechargeAtMilliseconds <= 0)
        {
            state.NextRechargeAtMilliseconds = nowMilliseconds + Math.Max(1L, rechargeMilliseconds);
        }

        snapshot = ToSnapshot(state, maximum);
        return true;
    }

    private ChargeState GetOrCreate(string playerUid, string skillCode, int maximum)
    {
        if (!states.TryGetValue(playerUid, out Dictionary<string, ChargeState>? playerStates))
        {
            playerStates = new Dictionary<string, ChargeState>(StringComparer.OrdinalIgnoreCase);
            states[playerUid] = playerStates;
        }

        if (!playerStates.TryGetValue(skillCode, out ChargeState? state))
        {
            state = new ChargeState { Current = Math.Max(1, maximum) };
            playerStates[skillCode] = state;
        }

        return state;
    }

    private static void Refresh(ChargeState state, int maximum, long rechargeMilliseconds, long nowMilliseconds)
    {
        int limit = Math.Max(1, maximum);
        long recharge = Math.Max(1L, rechargeMilliseconds);
        state.Current = Math.Clamp(state.Current, 0, limit);
        if (state.Current >= limit)
        {
            state.NextRechargeAtMilliseconds = 0;
            return;
        }

        if (state.NextRechargeAtMilliseconds <= 0)
        {
            state.NextRechargeAtMilliseconds = nowMilliseconds + recharge;
            return;
        }

        if (nowMilliseconds < state.NextRechargeAtMilliseconds)
        {
            return;
        }

        long recovered = 1L + (nowMilliseconds - state.NextRechargeAtMilliseconds) / recharge;
        state.Current = Math.Min(limit, state.Current + (int)Math.Min(int.MaxValue, recovered));
        state.NextRechargeAtMilliseconds = state.Current >= limit
            ? 0
            : state.NextRechargeAtMilliseconds + recovered * recharge;
    }

    private static SkillChargeSnapshot ToSnapshot(ChargeState state, int maximum)
    {
        return new SkillChargeSnapshot(
            state.Current,
            Math.Max(1, maximum),
            state.NextRechargeAtMilliseconds);
    }

    private sealed class ChargeState
    {
        public int Current { get; set; }
        public long NextRechargeAtMilliseconds { get; set; }
    }
}
