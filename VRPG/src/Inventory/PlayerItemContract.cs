using System;
using System.Collections.Generic;

namespace VRPG.Inventory;

/// <summary>Versions canonical inventory records independently from save containers and packets.</summary>
public static class PlayerItemSchema
{
    public const int CurrentVersion = 1;
}

/// <summary>Stable category codes used by storage, packets, and addon classification.</summary>
public static class PlayerItemCategories
{
    public const string Gear = "gear";
    public const string Materials = "materials";
    public const string Consumables = "consumables";
    public const string KeysAndQuest = "keysquest";
    public const string Miscellaneous = "miscellaneous";

    public static IReadOnlyList<string> Required { get; } = new[]
    {
        Gear,
        Materials,
        Consumables,
        KeysAndQuest,
        Miscellaneous
    };

    public static string NormalizeOrFallback(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return Miscellaneous;
        string normalized = category.Trim().ToLowerInvariant();
        foreach (string required in Required)
        {
            if (string.Equals(required, normalized, StringComparison.Ordinal)) return required;
        }

        return Miscellaneous;
    }
}

/// <summary>Stable equipment role codes. Addons may register additional roles later.</summary>
public static class PlayerEquipmentRoles
{
    public const string MainWeapon = "weapon.main";
    public const string AlternateWeapon1 = "weapon.alternate1";
    public const string AlternateWeapon2 = "weapon.alternate2";
    public const string Offhand = "weapon.offhand";
    public const string Head = "armor.head";
    public const string Body = "armor.body";
    public const string Legs = "armor.legs";
    public const string Hands = "armor.hands";
    public const string Feet = "armor.feet";
    public const string Accessory1 = "accessory.1";
    public const string Accessory2 = "accessory.2";
    public const string CosmeticHead = "cosmetic.head";
    public const string CosmeticShoulder = "cosmetic.shoulder";
    public const string CosmeticUpperBody = "cosmetic.upperbody";
    public const string CosmeticUpperBodyOver = "cosmetic.upperbodyover";
    public const string CosmeticLowerBody = "cosmetic.lowerbody";
    public const string CosmeticFeet = "cosmetic.feet";
    public const string CosmeticNeck = "cosmetic.neck";
    public const string CosmeticEmblem = "cosmetic.emblem";
    public const string CosmeticFace = "cosmetic.face";
    public const string CosmeticArms = "cosmetic.arms";
    public const string CosmeticHands = "cosmetic.hands";
    public const string CosmeticWaist = "cosmetic.waist";

    public static IReadOnlySet<string> BuiltIn { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        MainWeapon,
        AlternateWeapon1,
        AlternateWeapon2,
        Offhand,
        Head,
        Body,
        Legs,
        Hands,
        Feet,
        Accessory1,
        Accessory2,
        CosmeticHead,
        CosmeticShoulder,
        CosmeticUpperBody,
        CosmeticUpperBodyOver,
        CosmeticLowerBody,
        CosmeticFeet,
        CosmeticNeck,
        CosmeticEmblem,
        CosmeticFace,
        CosmeticArms,
        CosmeticHands,
        CosmeticWaist
    };
}

/// <summary>Capacity separates unique records from aggregate entry and quantity limits.</summary>
public sealed record PlayerItemCapacity(
    int UniqueEntryLimit,
    int AggregateEntryLimit,
    long AggregateQuantityPerEntry)
{
    public static PlayerItemCapacity Default { get; } = new(
        UniqueEntryLimit: 120,
        AggregateEntryLimit: 512,
        AggregateQuantityPerEntry: long.MaxValue);

    public bool IsValid => UniqueEntryLimit > 0
        && AggregateEntryLimit > 0
        && AggregateQuantityPerEntry > 0;
}

/// <summary>Canonical persisted payload. Quantity and entry identity belong to the store record.</summary>
public sealed record CanonicalItemPayload(
    int SchemaVersion,
    PlayerItemAssetKind AssetKind,
    string AssetCode,
    byte[] ExactState,
    CanonicalCollectibleReference[]? EmbeddedReferences = null);

/// <summary>Maps dynamic registry IDs found inside synchronized nested stack attributes.</summary>
public sealed record CanonicalCollectibleReference(
    PlayerItemAssetKind AssetKind,
    int SourceRegistryId,
    string AssetCode);

[Flags]
public enum PlayerItemFlags
{
    None = 0,
    Favorite = 1 << 0,
    Protected = 1 << 1,
    Junk = 1 << 2
}

[Flags]
public enum PlayerItemProtectionSource
{
    None = 0,
    User = 1,
    System = 2
}

/// <summary>Unsafe evolving content stays frozen in recovery until an audited adapter exists.</summary>
public enum PlayerItemStatePolicy
{
    ExactSnapshot,
    FrozenInRecovery
}

public sealed record PlayerItemCapacitySummary(
    long Revision,
    int UniqueEntriesUsed,
    int AggregateEntriesUsed,
    PlayerItemCapacity Limits);

/// <summary>Stable addon identity is independent of its display label.</summary>
public sealed record PlayerItemCategoryDefinition(string Code, string DisplayName, int Order = 0);

/// <summary>An immutable registry snapshot. Missing definitions affect presentation only.</summary>
public sealed class PlayerItemCategoryRegistry
{
    private readonly Dictionary<string, PlayerItemCategoryDefinition> definitions =
        new(StringComparer.OrdinalIgnoreCase);

    public PlayerItemCategoryRegistry(IEnumerable<PlayerItemCategoryDefinition>? addons = null)
    {
        foreach (string code in PlayerItemCategories.Required)
            definitions.Add(code, new PlayerItemCategoryDefinition(code, code));
        if (addons == null) return;
        foreach (PlayerItemCategoryDefinition definition in addons)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.Code)
                || string.IsNullOrWhiteSpace(definition.DisplayName))
                throw new ArgumentException("Category definitions require stable codes and display names.", nameof(addons));
            string code = definition.Code.Trim().ToLowerInvariant();
            if (!definitions.TryAdd(code, definition with { Code = code }))
                throw new ArgumentException("Category codes must be distinct from registered and built-in codes.", nameof(addons));
        }
    }

    public PlayerItemCategoryDefinition Resolve(string? authoredCategory) =>
        definitions.TryGetValue(authoredCategory?.Trim() ?? "", out PlayerItemCategoryDefinition? definition)
            ? definition : definitions[PlayerItemCategories.Miscellaneous];
}

/// <summary>The canonical stored entry; localized display data is resolved separately.</summary>
public sealed record CanonicalPlayerItemRecord(
    int SchemaVersion,
    string EntryId,
    PlayerItemStorageKind StorageKind,
    string Category,
    CanonicalItemPayload Payload,
    long Quantity,
    PlayerItemFlags Flags);

/// <summary>Equipment references the same canonical entry instead of copying its payload.</summary>
public sealed record EquippedPlayerItemRecord(string RoleCode, string EntryId);

public enum PlayerItemRecoveryReason
{
    CapacityExceeded,
    FailedMigration,
    MissingAsset,
    UnsupportedContent,
    InterruptedTransaction
}

/// <summary>An inaccessible entry remains intact and has an explicit recovery reason.</summary>
public sealed record RecoverablePlayerItemRecord(
    CanonicalPlayerItemRecord Item,
    PlayerItemRecoveryReason Reason,
    string Source);

public sealed record PlayerItemClassification(
    PlayerItemStorageKind StorageKind,
    string Category);

/// <summary>Engine facts used by deterministic classification without coupling rules to UI code.</summary>
public sealed record PlayerItemClassificationContext(
    PlayerItemAssetKind AssetKind,
    string AssetCode,
    int EngineMaxStackSize,
    bool IsTool,
    bool IsConsumable,
    PlayerItemStatePolicy StatePolicy = PlayerItemStatePolicy.ExactSnapshot);

/// <summary>Deterministic baseline with exact-code addon overrides and a safe fallback.</summary>
public sealed class PlayerItemClassifier
{
    private readonly Dictionary<string, PlayerItemClassification> overrides =
        new(StringComparer.OrdinalIgnoreCase);

    public void Register(string assetCode, PlayerItemClassification classification)
    {
        if (string.IsNullOrWhiteSpace(assetCode))
            throw new ArgumentException("An asset code is required.", nameof(assetCode));
        ArgumentNullException.ThrowIfNull(classification);

        string category = string.IsNullOrWhiteSpace(classification.Category)
            ? PlayerItemCategories.Miscellaneous : classification.Category.Trim().ToLowerInvariant();
        overrides[assetCode.Trim()] = classification with { Category = category };
    }

    public PlayerItemClassification Classify(PlayerItemClassificationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(context.AssetCode))
            throw new ArgumentException("An asset code is required.", nameof(context));

        if (overrides.TryGetValue(context.AssetCode.Trim(), out PlayerItemClassification? configured))
            return configured;

        if (context.IsTool || context.EngineMaxStackSize <= 1)
            return new PlayerItemClassification(PlayerItemStorageKind.Unique, PlayerItemCategories.Gear);
        if (context.IsConsumable)
            return new PlayerItemClassification(PlayerItemStorageKind.Consumable, PlayerItemCategories.Consumables);
        if (context.AssetKind == PlayerItemAssetKind.Block)
            return new PlayerItemClassification(PlayerItemStorageKind.Aggregate, PlayerItemCategories.Materials);

        return new PlayerItemClassification(
            PlayerItemStorageKind.Aggregate,
            PlayerItemCategories.Miscellaneous);
    }
}
