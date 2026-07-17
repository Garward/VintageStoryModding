using ProtoBuf;

namespace VRPG.Network;

[ProtoContract]
public sealed class TalentTreeSnapshotPacket
{
    public const int CurrentSchemaVersion = 1;

    [ProtoMember(1)]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [ProtoMember(2)]
    public string TreeCode { get; set; } = "";

    [ProtoMember(3)]
    public string ContentHash { get; set; } = "";

    [ProtoMember(4)]
    public TalentTreeNodePacket[] Nodes { get; set; } = System.Array.Empty<TalentTreeNodePacket>();

    [ProtoMember(5)]
    public string TreeName { get; set; } = "";
}

[ProtoContract]
public sealed class TalentTreeRequestPacket
{
    [ProtoMember(1)]
    public string KnownTreeCode { get; set; } = "";

    [ProtoMember(2)]
    public string KnownContentHash { get; set; } = "";
}

[ProtoContract]
public sealed class TalentProgressPacket
{
    [ProtoMember(1)]
    public string[] Talents { get; set; } = System.Array.Empty<string>();
}

[ProtoContract]
public sealed class TalentTreeNodePacket
{
    [ProtoMember(1)]
    public string Code { get; set; } = "";

    [ProtoMember(2)]
    public string Name { get; set; } = "";

    [ProtoMember(3)]
    public string Description { get; set; } = "";

    [ProtoMember(4)]
    public int X { get; set; }

    [ProtoMember(5)]
    public int Y { get; set; }

    [ProtoMember(6)]
    public string[] Links { get; set; } = System.Array.Empty<string>();

    [ProtoMember(7)]
    public int Cost { get; set; }

    [ProtoMember(8)]
    public bool Keystone { get; set; }

    [ProtoMember(9)]
    public string[] Modifiers { get; set; } = System.Array.Empty<string>();

    [ProtoMember(10)]
    public string VisualTier { get; set; } = "normal";

    [ProtoMember(11)]
    public bool Starter { get; set; }

    [ProtoMember(12)]
    public string Foundation { get; set; } = "";
}
