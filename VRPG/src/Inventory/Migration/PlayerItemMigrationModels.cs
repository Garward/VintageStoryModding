using System;
using VRPG.Inventory.Persistence;

namespace VRPG.Inventory.Migration;

public static class PlayerItemMigrationSchema
{
    public const int CurrentVersion = 1;
}

public enum LegacyInventoryState
{
    MatchesSnapshot,
    Empty,
    Changed
}

public sealed record LegacyInventoryInspection(
    string PlayerUid,
    LegacyItemSlotState[] Slots,
    string[] BlockingReasons);

public sealed record LegacyInventoryMutationResult(bool Success, string Error)
{
    public static LegacyInventoryMutationResult Accepted() => new(true, "");
    public static LegacyInventoryMutationResult Rejected(string error) => new(false, error);
}

public interface ILegacyPlayerInventorySource
{
    string PlayerUid { get; }
    LegacyInventoryInspection Inspect();
    LegacyInventoryState CompareTo(LegacyItemSlotState[] snapshot);
    LegacyInventoryMutationResult Clear(LegacyItemSlotState[] expected);
    LegacyInventoryMutationResult Restore(LegacyItemSlotState[] snapshot);
}

public sealed record PlayerItemMigrationReport(
    bool Ready,
    bool AlreadyCompleted,
    int StackCount,
    long ItemCount,
    int StoredEntries,
    int OverflowEntries,
    int UnresolvedEntries,
    string[] BlockingReasons)
{
    public string Summary()
    {
        if (AlreadyCompleted) return "This player's inventory is already migrated.";
        if (!Ready)
        {
            string reason = BlockingReasons.Length == 0 ? "The inventory could not be verified." : BlockingReasons[0];
            return "Migration blocked: " + reason;
        }
        string recovery = OverflowEntries == 0
            ? "Nothing will enter Recovery."
            : $"{OverflowEntries} stack(s) will enter Recovery.";
        return $"Ready: {StackCount} stack(s), {ItemCount} item(s). {recovery}";
    }
}

public sealed record PlayerItemMigrationPlan(
    PlayerItemMigrationReport Report,
    InMemoryPlayerItemStore? StagedStore,
    PlayerItemMigrationJournalState? Journal);

public sealed record PlayerItemMigrationResult(bool Success, bool RecoveryRequired, string Message);
