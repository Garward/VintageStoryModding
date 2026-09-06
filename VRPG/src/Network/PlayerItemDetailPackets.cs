using System;
using ProtoBuf;

namespace VRPG.Network;

[ProtoContract]
public sealed class PlayerItemDetailRequest
{
    [ProtoMember(1)] public long ConnectionGeneration { get; set; }
    [ProtoMember(2)] public long QueryId { get; set; }
    [ProtoMember(3)] public string EntryId { get; set; } = "";
    [ProtoMember(4)] public string RoleCode { get; set; } = "";
    [ProtoMember(5)] public int ProtocolVersion { get; set; } = 1;
}

[ProtoContract]
public sealed class PlayerItemDetailResult
{
    [ProtoMember(1)] public long ConnectionGeneration { get; set; }
    [ProtoMember(2)] public long QueryId { get; set; }
    [ProtoMember(3)] public string EntryId { get; set; } = "";
    [ProtoMember(4)] public string RoleCode { get; set; } = "";
    [ProtoMember(5)] public long StoreRevision { get; set; }
    [ProtoMember(6)] public long BuildRevision { get; set; }
    [ProtoMember(7)] public PlayerItemEffectPacket[] Effects { get; set; } = Array.Empty<PlayerItemEffectPacket>();
    [ProtoMember(8)] public PlayerItemComparisonPacket[] Comparison { get; set; } = Array.Empty<PlayerItemComparisonPacket>();
    [ProtoMember(9)] public PlayerItemActionAvailability[] Actions { get; set; } = Array.Empty<PlayerItemActionAvailability>();
    [ProtoMember(10)] public string RejectionCode { get; set; } = "";
    [ProtoMember(11)] public int RequiredLevel { get; set; }
    [ProtoMember(12)] public int ReferenceLevel { get; set; }
    [ProtoMember(13)] public string[] GainedTransformations { get; set; } = Array.Empty<string>();
    [ProtoMember(14)] public string[] LostTransformations { get; set; } = Array.Empty<string>();
}

[ProtoContract]
public sealed class PlayerItemEffectPacket
{
    [ProtoMember(1)] public string StatCode { get; set; } = "";
    [ProtoMember(2)] public string Operation { get; set; } = "";
    [ProtoMember(3)] public double Value { get; set; }
    [ProtoMember(4)] public string UnitCode { get; set; } = "";
    [ProtoMember(5)] public string ConditionCode { get; set; } = "";
}

[ProtoContract]
public sealed class PlayerItemComparisonPacket
{
    [ProtoMember(1)] public string StatCode { get; set; } = "";
    [ProtoMember(2)] public double Before { get; set; }
    [ProtoMember(3)] public double After { get; set; }
    [ProtoMember(4)] public double Difference { get; set; }
    [ProtoMember(5)] public string UnitCode { get; set; } = "";
    [ProtoMember(6)] public string ConditionCode { get; set; } = "";
}

[ProtoContract]
public sealed class PlayerItemActionAvailability
{
    [ProtoMember(1)] public PlayerItemAction Action { get; set; }
    [ProtoMember(2)] public bool Enabled { get; set; }
    [ProtoMember(3)] public string ReasonCode { get; set; } = "";
}
