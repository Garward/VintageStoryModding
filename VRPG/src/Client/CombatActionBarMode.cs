using Vintagestory.API.Common;

namespace VRPG.Client;

/// <summary>Defines the temporary boundary between Vanilla and VRPG action-bar ownership.</summary>
public static class CombatActionBarMode
{
    public static bool IsActive(bool replacementEnabled, EnumGameMode gameMode)
    {
        return replacementEnabled && gameMode == EnumGameMode.Survival;
    }
}
