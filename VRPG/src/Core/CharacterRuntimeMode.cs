using Vintagestory.API.Common;

namespace VRPG.Core;

/// <summary>Temporary realm boundary for VRPG-owned character mechanics.</summary>
public static class CharacterRuntimeMode
{
    public static bool IsOwned(bool rpgEnabled, bool authorityEnabled, EnumGameMode gameMode)
    {
        return rpgEnabled && authorityEnabled && gameMode == EnumGameMode.Survival;
    }
}
