using VRPG.Inventory;
using VRPG.Network;

namespace VRPG.Client.PlayerMenu.Inventory;

public static class RecoveryView
{
    public static bool IsRecovery(PlayerItemLocation location) => location is PlayerItemLocation.Overflow or PlayerItemLocation.Unresolved;
    public static string ReasonKey(PlayerItemEntryPacket entry) => entry.Location == PlayerItemLocation.Unresolved
        ? "recovery-unresolved" : "recovery-overflow";
}
