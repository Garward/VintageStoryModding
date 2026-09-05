using System;

namespace VRPG.Inventory;

public enum PlayerItemStorageKind
{
    Unique,
    Aggregate,
    Consumable,
    Ledger,
    Protected
}

public enum PlayerItemAssetKind
{
    Item,
    Block
}

public enum PlayerItemLocation
{
    Stored,
    Equipped,
    Overflow,
    Unresolved
}

public enum PlayerItemSortOrder
{
    Name,
    QuantityDescending,
    AssetCode
}

/// <summary>Canonical identity and display metadata supplied to the item store.</summary>
public sealed record PlayerItemPrototype(
    PlayerItemStorageKind Kind,
    string AssetCode,
    string Category,
    string DisplayName,
    byte[] ExactState,
    PlayerItemAssetKind AssetKind = PlayerItemAssetKind.Item,
    CanonicalCollectibleReference[]? EmbeddedReferences = null);

/// <summary>Immutable, bounded information safe for inventory query results.</summary>
public sealed record PlayerItemSummary(
    string EntryId,
    PlayerItemStorageKind Kind,
    string AssetCode,
    string Category,
    string DisplayName,
    long Quantity,
    PlayerItemAssetKind AssetKind = PlayerItemAssetKind.Item,
    PlayerItemLocation Location = PlayerItemLocation.Stored,
    PlayerItemFlags Flags = PlayerItemFlags.None,
    long ReservedQuantity = 0);

/// <summary>Clone-owned canonical entry data for trusted server-side consumers.</summary>
public sealed record PlayerItemDetail(
    string EntryId,
    PlayerItemPrototype Prototype,
    long Quantity,
    PlayerItemLocation Location,
    PlayerItemFlags Flags);

public sealed record PlayerItemQuery(
    string Category,
    string Search,
    int Offset,
    int Limit,
    PlayerItemLocation Location = PlayerItemLocation.Stored,
    PlayerItemSortOrder Sort = PlayerItemSortOrder.Name);

public sealed record PlayerItemPage(
    long Revision,
    int Total,
    PlayerItemSummary[] Entries);

public enum PlayerItemMutationRejection
{
    None,
    InvalidRequest,
    StaleRevision,
    EntryMissing,
    InsufficientQuantity,
    QuantityOverflow,
    RequestIdConflict,
    EntryUnavailable,
    EntryReserved,
    EntryProtected,
    CapacityExceeded
}

public sealed record PlayerItemMutationResult(
    bool Success,
    long Revision,
    string EntryId,
    long Quantity,
    PlayerItemMutationRejection Rejection,
    string RequestId = "")
{
    public static PlayerItemMutationResult Rejected(
        long revision,
        PlayerItemMutationRejection rejection,
        string requestId = "") =>
        new(false, revision, "", 0, rejection, requestId);
}
