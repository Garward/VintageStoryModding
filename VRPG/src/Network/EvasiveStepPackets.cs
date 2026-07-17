using ProtoBuf;

namespace VRPG.Network;

[ProtoContract]
public sealed class EvasiveStepActivatedPacket
{
    [ProtoMember(1)]
    public double MotionX { get; set; }

    [ProtoMember(2)]
    public double MotionZ { get; set; }
}
