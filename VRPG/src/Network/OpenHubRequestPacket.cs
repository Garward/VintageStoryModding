using ProtoBuf;

namespace VRPG.Network;

[ProtoContract]
public sealed class OpenHubRequestPacket
{
    [ProtoMember(1)]
    public int RequestId { get; set; }

    [ProtoMember(2)]
    public string KnownTalentTreeCode { get; set; } = "";

    [ProtoMember(3)]
    public string KnownTalentTreeHash { get; set; } = "";
}
