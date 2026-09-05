using System;
using Vintagestory.API.Common;

namespace VRPG.Core;

/// <summary>Resolves movement stats owned by the current VRPG gameplay profile.</summary>
public static class CharacterMovementAuthority
{
    public const string WalkSpeedStat = "walkspeed";
    public const string JumpHeightStat = "jumpHeightMul";

    public static bool TryResolve(
        bool rpgEnabled,
        bool authorityEnabled,
        EnumGameMode gameMode,
        string stat,
        float walkSpeedMultiplier,
        float jumpHeightMultiplier,
        out float value)
    {
        value = 1f;
        if (!CharacterRuntimeMode.IsOwned(rpgEnabled, authorityEnabled, gameMode))
        {
            return false;
        }

        if (string.Equals(stat, WalkSpeedStat, StringComparison.Ordinal))
        {
            value = Math.Clamp(walkSpeedMultiplier, 0.1f, 5f);
            return true;
        }

        if (string.Equals(stat, JumpHeightStat, StringComparison.Ordinal))
        {
            value = Math.Clamp(jumpHeightMultiplier, 1f, 9f);
            return true;
        }

        return false;
    }
}
