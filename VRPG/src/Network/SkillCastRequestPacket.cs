using ProtoBuf;

namespace VRPG.Network;

[ProtoContract]
public sealed class SkillCastRequestPacket
{
    [ProtoMember(1)]
    public int Slot { get; set; }

    [ProtoMember(2)]
    public bool Pressed { get; set; } = true;
}
