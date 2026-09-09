using System;
using ProtoBuf;
using VRPG.Inventory;

namespace VRPG.Network;

/// <summary>Append fields only. These contracts never transport canonical item state or player UID.</summary>
[ProtoContract]
public sealed class PlayerItemQueryRequest
{
    [ProtoMember(1)] public int ProtocolVersion { get; set; } = 1;
    [ProtoMember(2)] public long ConnectionGeneration { get; set; }
    [ProtoMember(3)] public long QueryId { get; set; }
    [ProtoMember(4)] public string Category { get; set; } = "";
    [ProtoMember(5)] public PlayerItemLocation Location { get; set; }
    [ProtoMember(6)] public string Search { get; set; } = "";
    [ProtoMember(7)] public PlayerItemFilters Filters { get; set; }
    [ProtoMember(8)] public PlayerItemSortOrder Sort { get; set; }
    [ProtoMember(9)] public int Offset { get; set; }
    [ProtoMember(10)] public int Limit { get; set; } = 64;
    [ProtoMember(11)] public int CategoryOffset { get; set; }
    [ProtoMember(12)] public int CategoryLimit { get; set; } = 64;
}

[ProtoContract]
public sealed class PlayerItemQueryResult
{
    [ProtoMember(1)] public long ConnectionGeneration { get; set; }
    [ProtoMember(2)] public long QueryId { get; set; }
    [ProtoMember(3)] public long StoreRevision { get; set; }
    [ProtoMember(4)] public int Total { get; set; }
    [ProtoMember(5)] public PlayerItemEntryPacket[] Entries { get; set; } = Array.Empty<PlayerItemEntryPacket>();
    [ProtoMember(6)] public PlayerItemCapacityPacket CapacitySummary { get; set; } = new();
    [ProtoMember(7)] public int RecoveryCount { get; set; }
    [ProtoMember(8)] public string RejectionCode { get; set; } = "";
    [ProtoMember(9)] public long BuildRevision { get; set; }
    [ProtoMember(10)] public PlayerItemCategoryPacket[] Categories { get; set; } = Array.Empty<PlayerItemCategoryPacket>();
    [ProtoMember(11)] public int CategoryTotal { get; set; }
    [ProtoMember(12)] public PlayerItemCategoryPacket[] CoreCategories { get; set; } = Array.Empty<PlayerItemCategoryPacket>();
}

[ProtoContract]
public sealed class PlayerItemCategoryPacket
{
    [ProtoMember(1)] public string Code { get; set; } = "";
    [ProtoMember(2)] public string DisplayName { get; set; } = "";
    [ProtoMember(3)] public int Count { get; set; }
}

[ProtoContract]
public sealed class PlayerItemEntryPacket
{
    [ProtoMember(1)] public string EntryId { get; set; } = "";
    [ProtoMember(2)] public string AssetCode { get; set; } = "";
    [ProtoMember(3)] public string Category { get; set; } = "";
    [ProtoMember(4)] public string DisplayName { get; set; } = "";
    [ProtoMember(5)] public long Quantity { get; set; }
    [ProtoMember(6)] public long ReservedQuantity { get; set; }
    [ProtoMember(7)] public PlayerItemStorageKind Kind { get; set; }
    [ProtoMember(8)] public PlayerItemLocation Location { get; set; }
    [ProtoMember(9)] public PlayerItemFlags Flags { get; set; }
    [ProtoMember(10)] public PlayerItemProtectionSource ProtectionSource { get; set; }
    [ProtoMember(11)] public string AuthoredCategory { get; set; } = "";
    [ProtoMember(12)] public PlayerItemAssetKind AssetKind { get; set; }
    [ProtoMember(13)] public string CategoryName { get; set; } = "";
}

[ProtoContract]
public sealed class PlayerItemCapacityPacket
{
    [ProtoMember(1)] public int UniqueEntriesUsed { get; set; }
    [ProtoMember(2)] public int AggregateEntriesUsed { get; set; }
    [ProtoMember(3)] public int UniqueEntryLimit { get; set; }
    [ProtoMember(4)] public int AggregateEntryLimit { get; set; }
    [ProtoMember(5)] public long AggregateQuantityPerEntry { get; set; }
}
