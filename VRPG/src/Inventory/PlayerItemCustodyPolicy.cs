using System;
using System.Collections.Generic;
using VRPG.Inventory.Persistence;

namespace VRPG.Inventory;

public enum PlayerItemCustodyOwner { Recovery, Vanilla, Canonical }

/// <summary>Custody is separate from permission to perform a canonical operation.</summary>
public sealed record PlayerItemCustodySnapshot(PlayerItemCustodyOwner Owner, bool AllowsCanonical)
{
    public bool AllowsVanilla => Owner == PlayerItemCustodyOwner.Vanilla;
    public static PlayerItemCustodySnapshot Unresolved { get; } = new(PlayerItemCustodyOwner.Recovery, false);
}

/// <summary>World-scoped phase evidence. Unknown archive state never means no migration.</summary>
public sealed class PlayerItemCustodyPolicy
{
    private readonly Dictionary<string, PlayerItemCustodyOwner> observed = new(StringComparer.OrdinalIgnoreCase);

    public PlayerItemCustodySnapshot Observe(string? playerUid, bool archiveValidated,
        PlayerItemMigrationStatus? phase, bool persistenceReady, bool bridgesReady)
    {
        if (string.IsNullOrWhiteSpace(playerUid)) return PlayerItemCustodySnapshot.Unresolved;
        string uid = playerUid.Trim();
        if (!archiveValidated)
            return observed.TryGetValue(uid, out var previous) && previous == PlayerItemCustodyOwner.Canonical
                ? new(previous, false) : PlayerItemCustodySnapshot.Unresolved;
        var owner = phase switch
        {
            null or PlayerItemMigrationStatus.Restored => PlayerItemCustodyOwner.Vanilla,
            PlayerItemMigrationStatus.Committed => PlayerItemCustodyOwner.Canonical,
            _ => PlayerItemCustodyOwner.Recovery
        };
        observed[uid] = owner;
        return new(owner, owner == PlayerItemCustodyOwner.Canonical && persistenceReady && bridgesReady);
    }

    public void Forget(string playerUid) => observed.Remove(playerUid.Trim());
    public void Reset() => observed.Clear();
}
