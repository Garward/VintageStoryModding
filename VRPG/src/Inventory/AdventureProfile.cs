using System;
using VRPG.Inventory.Migration;
using VRPG.Inventory.Persistence;

namespace VRPG.Inventory;

/// <summary>The current server profile is Adventure; a future realm service can replace this decision.</summary>
public sealed class AdventureProfile(bool enabled, Func<PlayerItemArchiveService?> archive,
    Func<bool> protectionReady, Func<string, PlayerItemCustodySnapshot> custody)
{
    public bool Enabled { get; } = enabled;
    public static bool IsEnabled(bool rpgEnabled, bool inventoryReplacementEnabled) =>
        rpgEnabled && inventoryReplacementEnabled;

    public bool IsReady => Enabled && archive() is { IsLoaded: true, IsCrashDurable: true } && protectionReady();

    public bool CanParticipate(string playerUid, bool menuReady) =>
        IsReady && menuReady && custody(playerUid) is { Owner: PlayerItemCustodyOwner.Canonical, AllowsCanonical: true };

    public PlayerItemMigrationResult Prepare(ILegacyPlayerInventorySource source, bool menuReady)
    {
        try
        {
            if (!IsReady || !menuReady)
                return new(false, false, "Adventure storage, native protection, or menu service is unavailable.");
            var result = new PlayerItemMigrationCoordinator(archive()!).Commit(source);
            if (!result.Success) return result;
            return CanParticipate(source.PlayerUid, menuReady)
                ? result : new(false, true, "Migration completed but canonical custody is unavailable.");
        }
        catch (Exception error)
        {
            return new(false, true, "Adventure admission failed: " + error);
        }
    }
}
