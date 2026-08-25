using ProtoBuf;

namespace ResponsiveVS.Network.Messages;

[ProtoContract]
public sealed class ResponsiveDiagToggle
{
    [ProtoMember(1)] public int Level { get; set; }
    [ProtoMember(2)] public string Reason { get; set; }
}
