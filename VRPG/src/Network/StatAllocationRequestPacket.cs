using ProtoBuf;

namespace VRPG.Network;

[ProtoContract]
public sealed class StatAllocationRequestPacket
{
    [ProtoMember(1)]
    public string StatCode { get; set; } = "";

    [ProtoMember(2)]
    public int Delta { get; set; }
}
