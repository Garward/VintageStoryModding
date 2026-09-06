using System;
using ProtoBuf;

namespace VRPG.Network;

public enum PlayerItemAction { None, Equip, Unequip, Favorite, Protect, Junk, Recover, Destroy, Transfer, Use }

[ProtoContract]
public sealed class PlayerItemActionRequest
{
    [ProtoMember(1)] public string RequestId { get; set; } = "";
    [ProtoMember(2)] public PlayerItemAction Action { get; set; }
    [ProtoMember(3)] public long ExpectedStoreRevision { get; set; }
    [ProtoMember(4)] public long ExpectedBuildRevision { get; set; }
    [ProtoMember(5)] public string EntryId { get; set; } = "";
    [ProtoMember(6)] public string RoleCode { get; set; } = "";
    [ProtoMember(7)] public string DestinationId { get; set; } = "";
    [ProtoMember(8)] public long Quantity { get; set; }
    [ProtoMember(9)] public string PreviewToken { get; set; } = "";
    [ProtoMember(10)] public long ConnectionGeneration { get; set; }
    [ProtoMember(11)] public int ProtocolVersion { get; set; } = 1;
    [ProtoMember(12)] public long RequestSequence { get; set; }
}

[ProtoContract]
public sealed class PlayerItemActionResult
{
    [ProtoMember(1)] public string RequestId { get; set; } = "";
    [ProtoMember(2)] public bool Success { get; set; }
    [ProtoMember(3)] public string RejectionCode { get; set; } = "";
    [ProtoMember(4)] public long StoreRevision { get; set; }
    [ProtoMember(5)] public long BuildRevision { get; set; }
    [ProtoMember(6)] public PlayerItemEntryPacket[] ChangedEntries { get; set; } = Array.Empty<PlayerItemEntryPacket>();
    [ProtoMember(7)] public string[] InvalidatedScopes { get; set; } = Array.Empty<string>();
    [ProtoMember(8)] public long ConnectionGeneration { get; set; }
}

/// <summary>Server creates generation on connection; zero is never write-ready.</summary>
[ProtoContract]
public sealed class PlayerItemSessionPacket
{
    [ProtoMember(1)] public int ProtocolVersion { get; set; } = 1;
    [ProtoMember(2)] public long ConnectionGeneration { get; set; }
}

[ProtoContract]
public sealed class PlayerItemInvalidationPacket
{
    [ProtoMember(1)] public long ConnectionGeneration { get; set; }
    [ProtoMember(2)] public long StoreRevision { get; set; }
    [ProtoMember(3)] public long BuildRevision { get; set; }
    [ProtoMember(4)] public string[] Scopes { get; set; } = Array.Empty<string>();
}
