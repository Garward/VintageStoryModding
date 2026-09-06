using System;

namespace VRPG.Inventory;

/// <summary>Shared custody rules; presentation categories and user flags cannot grant capacity.</summary>
public static class PlayerItemPolicy
{
    public static bool RequiresRecovery(PlayerItemPrototype prototype) =>
        prototype.StatePolicy == PlayerItemStatePolicy.FrozenInRecovery;

    public static PlayerItemProtectionSource LegacyProtection(PlayerItemPrototype prototype, PlayerItemFlags flags) =>
        RequiresSystemProtection(prototype) ? PlayerItemProtectionSource.System
            : flags.HasFlag(PlayerItemFlags.Protected) ? PlayerItemProtectionSource.User : PlayerItemProtectionSource.None;

    public static PlayerItemFlags EffectiveFlags(PlayerItemFlags flags, PlayerItemProtectionSource protection) =>
        protection.HasFlag(PlayerItemProtectionSource.System)
            ? (flags | PlayerItemFlags.Protected) & ~PlayerItemFlags.Junk
            : protection != PlayerItemProtectionSource.None ? flags | PlayerItemFlags.Protected
            : flags & ~PlayerItemFlags.Protected;

    public static bool UsesUniqueCapacity(PlayerItemPrototype prototype) =>
        prototype.Kind == PlayerItemStorageKind.Unique;

    public static bool UsesAggregateCapacity(PlayerItemPrototype prototype) =>
        prototype.Kind is PlayerItemStorageKind.Aggregate or PlayerItemStorageKind.Consumable;

    public static bool RequiresSystemProtection(PlayerItemPrototype prototype) =>
        prototype.Kind == PlayerItemStorageKind.Protected
        || string.Equals(prototype.Category?.Trim(), PlayerItemCategories.KeysAndQuest,
            StringComparison.OrdinalIgnoreCase);

    public static bool QuantityFits(PlayerItemPrototype prototype, long quantity, PlayerItemCapacity capacity) =>
        quantity > 0 && (UsesUniqueCapacity(prototype) ? quantity == 1
            : !UsesAggregateCapacity(prototype) || quantity <= capacity.AggregateQuantityPerEntry);

    public static bool HasRoom(PlayerItemPrototype prototype, int uniqueCount, int aggregateCount,
        PlayerItemCapacity capacity) =>
        UsesUniqueCapacity(prototype) ? uniqueCount < capacity.UniqueEntryLimit
            : !UsesAggregateCapacity(prototype) || aggregateCount < capacity.AggregateEntryLimit;
}
