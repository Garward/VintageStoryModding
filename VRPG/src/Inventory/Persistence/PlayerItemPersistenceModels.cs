using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VRPG.Inventory.Bridges;

namespace VRPG.Inventory.Persistence;

public static class PlayerItemPersistenceSchema
{
    public const int CurrentVersion = 3;
    public const int SlotFormatVersion = 1;
}

public sealed class PlayerItemArchive
{
    public int SchemaVersion { get; set; } = PlayerItemPersistenceSchema.CurrentVersion;
    public Dictionary<string, PlayerItemStoreState> Players { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, PlayerItemMigrationJournalState> Migrations { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, PlayerItemBridgeReceipt> Bridges { get; set; } = new(StringComparer.Ordinal);

    [JsonExtensionData]
    public IDictionary<string, JToken>? ExtensionData { get; set; }
}

public enum PlayerItemMigrationStatus
{
    Prepared,
    Committed,
    RestorePending,
    Restored
}

public sealed class PlayerItemMigrationJournalState
{
    public int MigrationVersion { get; set; }
    public PlayerItemMigrationStatus Status { get; set; }
    public PlayerItemStoreState PriorStore { get; set; } = new();
    public LegacyItemSlotState[] SourceSlots { get; set; } = Array.Empty<LegacyItemSlotState>();
    public int SourceStackCount { get; set; }
    public long SourceItemCount { get; set; }
    public int ImportedEntryCount { get; set; }
    public int OverflowEntryCount { get; set; }
    public int UnresolvedEntryCount { get; set; }

    [JsonExtensionData]
    public IDictionary<string, JToken>? ExtensionData { get; set; }
}

public enum LegacyInventoryArea
{
    Hotbar,
    Backpack,
    Character,
    Offhand,
    Mouse,
    Crafting
}

public sealed class LegacyItemSlotState
{
    public LegacyInventoryArea Area { get; set; }
    public string InventoryClass { get; set; } = "";
    public int SlotIndex { get; set; }
    public int ParentSlotIndex { get; set; } = -1;
    public PlayerItemPrototype Prototype { get; set; } = new(
        PlayerItemStorageKind.Aggregate,
        "invalid:missing",
        PlayerItemCategories.Miscellaneous,
        "Missing item",
        Array.Empty<byte>());
    public int Quantity { get; set; }
    public string EquipmentRole { get; set; } = "";

    [JsonExtensionData]
    public IDictionary<string, JToken>? ExtensionData { get; set; }
}

public sealed class PlayerItemStoreState
{
    public long Revision { get; set; }
    // Populate would mutate the shared default record through its init-only properties.
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public PlayerItemCapacity Capacity { get; set; } = PlayerItemCapacity.Default;
    public PlayerItemEntryState[] Entries { get; set; } = Array.Empty<PlayerItemEntryState>();
    public EquippedPlayerItemRecord[] Equipment { get; set; } = Array.Empty<EquippedPlayerItemRecord>();
    public PlayerItemReservationState[] Reservations { get; set; } =
        Array.Empty<PlayerItemReservationState>();
    public PlayerItemCompletedRequestState[] CompletedRequests { get; set; } =
        Array.Empty<PlayerItemCompletedRequestState>();

    [JsonExtensionData]
    public IDictionary<string, JToken>? ExtensionData { get; set; }
}

public sealed class PlayerItemEntryState
{
    public string EntryId { get; set; } = "";
    public PlayerItemPrototype Prototype { get; set; } = new(
        PlayerItemStorageKind.Aggregate,
        "invalid:missing",
        PlayerItemCategories.Miscellaneous,
        "Missing item",
        Array.Empty<byte>());
    public long Quantity { get; set; }
    public PlayerItemLocation Location { get; set; }
    public PlayerItemFlags Flags { get; set; }
    // Null identifies old records whose protection was encoded only as a flag.
    public PlayerItemProtectionSource? ProtectionSource { get; set; }
    public PlayerItemRecoveryReason RecoveryReason { get; set; }
    public string RecoverySource { get; set; } = "";

    [JsonExtensionData]
    public IDictionary<string, JToken>? ExtensionData { get; set; }
}

public sealed class PlayerItemReservationState
{
    public string ReservationId { get; set; } = "";
    public PlayerItemAmount[] Amounts { get; set; } = Array.Empty<PlayerItemAmount>();
    /// <summary>A migrated interrupted transaction retains custody and can only be rolled back.</summary>
    public bool RequiresRecovery { get; set; }

    [JsonExtensionData]
    public IDictionary<string, JToken>? ExtensionData { get; set; }
}

public enum PlayerItemPersistedResultKind
{
    Mutation,
    Operation,
    Reservation
}

public sealed class PlayerItemCompletedRequestState
{
    public string RequestId { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public PlayerItemPersistedResultKind ResultKind { get; set; }
    public bool Success { get; set; }
    public long Revision { get; set; }
    public string EntryId { get; set; } = "";
    public long Quantity { get; set; }
    public string RoleCode { get; set; } = "";
    public string ReservationId { get; set; } = "";
    public int Rejection { get; set; }

    [JsonExtensionData]
    public IDictionary<string, JToken>? ExtensionData { get; set; }
}

public sealed record PlayerItemStoreCapture(PlayerItemStoreState State, long PersistenceStamp);

public sealed record PlayerItemArchiveLoadResult(
    bool Success,
    bool IsNew,
    bool RecoveredFromOlderGeneration,
    long Generation,
    PlayerItemArchive Archive,
    string Error)
{
    public static PlayerItemArchiveLoadResult Empty() => new(
        true,
        true,
        false,
        0,
        new PlayerItemArchive(),
        "");

    public static PlayerItemArchiveLoadResult Failed(string error) => new(
        false,
        false,
        false,
        0,
        new PlayerItemArchive(),
        error);
}

public sealed record PlayerItemArchiveSaveResult(
    bool Success,
    long Generation,
    string Error)
{
    public static PlayerItemArchiveSaveResult Failed(long generation, string error) =>
        new(false, generation, error);
}
