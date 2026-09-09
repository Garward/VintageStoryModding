using System;
using ProtoBuf;
using VRPG.Parties;

namespace VRPG.Network;

[ProtoContract]
public sealed class PartySessionPacket
{
    [ProtoMember(1)] public string RequestId { get; set; } = "";
    [ProtoMember(2)] public string SessionId { get; set; } = "";
}

[ProtoContract]
public sealed class PartyQueryPacket
{
    [ProtoMember(1)] public string SessionId { get; set; } = "";
    [ProtoMember(2)] public string RequestId { get; set; } = "";
    [ProtoMember(3)] public string Search { get; set; } = "";
    [ProtoMember(4)] public int Page { get; set; }
    [ProtoMember(5)] public bool IncludeTargets { get; set; }
    [ProtoMember(6)] public long QuerySequence { get; set; }
}

[ProtoContract]
public sealed class PartyMutationPacket
{
    [ProtoMember(1)] public string SessionId { get; set; } = "";
    [ProtoMember(2)] public string RequestId { get; set; } = "";
    [ProtoMember(3)] public string PartyId { get; set; } = "";
    [ProtoMember(4)] public long ExpectedRevision { get; set; }
    [ProtoMember(5)] public PartyAction Action { get; set; }
    [ProtoMember(6)] public string TargetUid { get; set; } = "";
    [ProtoMember(7)] public string InvitationId { get; set; } = "";
}

[ProtoContract]
public sealed class PartyResultPacket
{
    [ProtoMember(1)] public string SessionId { get; set; } = "";
    [ProtoMember(2)] public string RequestId { get; set; } = "";
    [ProtoMember(3)] public bool Success { get; set; }
    [ProtoMember(4)] public string Code { get; set; } = "";
    [ProtoMember(5)] public PartySnapshotPacket? Snapshot { get; set; }
}

[ProtoContract]
public sealed class PartySnapshotPacket
{
    [ProtoMember(1)] public string SessionId { get; set; } = "";
    [ProtoMember(2)] public string RequestId { get; set; } = "";
    [ProtoMember(3)] public long Sequence { get; set; }
    [ProtoMember(4)] public PartyHealth Health { get; set; }
    [ProtoMember(5)] public long Revision { get; set; }
    [ProtoMember(6)] public string SelfUid { get; set; } = "";
    [ProtoMember(7)] public string PartyId { get; set; } = "";
    [ProtoMember(8)] public string LeaderUid { get; set; } = "";
    [ProtoMember(9)] public PartyMemberPacket[] Members { get; set; } = Array.Empty<PartyMemberPacket>();
    [ProtoMember(10)] public PartyInvitationPacket[] IncomingInvitations { get; set; } = Array.Empty<PartyInvitationPacket>();
    [ProtoMember(11)] public PartyTargetPacket[] Targets { get; set; } = Array.Empty<PartyTargetPacket>();
    [ProtoMember(12)] public string Search { get; set; } = "";
    [ProtoMember(13)] public int Page { get; set; }
    [ProtoMember(14)] public bool HasMoreTargets { get; set; }
    [ProtoMember(15)] public string Code { get; set; } = "ok";
}

[ProtoContract]
public sealed class PartyMemberPacket
{
    [ProtoMember(1)] public string Uid { get; set; } = "";
    [ProtoMember(2)] public string DisplayName { get; set; } = "";
    [ProtoMember(3)] public bool IsOnline { get; set; }
}

[ProtoContract]
public sealed class PartyInvitationPacket
{
    [ProtoMember(1)] public string InvitationId { get; set; } = "";
    [ProtoMember(2)] public string PartyId { get; set; } = "";
    [ProtoMember(3)] public string LeaderUid { get; set; } = "";
    [ProtoMember(4)] public string LeaderName { get; set; } = "";
    [ProtoMember(5)] public long ExpiresAtUnixSeconds { get; set; }
}

[ProtoContract]
public sealed class PartyTargetPacket
{
    [ProtoMember(1)] public string Uid { get; set; } = "";
    [ProtoMember(2)] public string DisplayName { get; set; } = "";
}
