using ProtoBuf;

namespace VRPG.Network;

public static class PlayerMenuCapabilityProtocol
{
    public const int CurrentVersion = 1;
}

[ProtoContract]
public sealed class CombatInteractionRequestPacket
{
    [ProtoMember(1)]
    public long RequestId { get; set; }

    [ProtoMember(2)]
    public long TargetEntityId { get; set; }

    [ProtoMember(3)]
    public string InteractionCode { get; set; } = "";

    [ProtoMember(4)]
    public int SelectionBoxIndex { get; set; }
}

[ProtoContract]
public sealed class CombatInteractionResultPacket
{
    [ProtoMember(1)]
    public long RequestId { get; set; }

    [ProtoMember(2)]
    public long TargetEntityId { get; set; }

    [ProtoMember(3)]
    public string InteractionCode { get; set; } = "";

    [ProtoMember(4)]
    public bool Success { get; set; }

    [ProtoMember(5)]
    public string RejectionCode { get; set; } = "";
}

[ProtoContract]
public sealed class PlayerMenuCapabilityPacket
{
    [ProtoMember(1)]
    public int ProtocolVersion { get; set; }

    [ProtoMember(2)]
    public bool InventoryReplacementEnabled { get; set; }

    [ProtoMember(3)]
    public bool ItemStoreReady { get; set; }

    [ProtoMember(4)]
    public bool DialogReplacementReady { get; set; }
}
