using ProtoBuf;

namespace ResponsiveVS.Network.Messages;

[ProtoContract]
public sealed class ResponsiveHandshakeResult
{
    [ProtoMember(1)] public int ProtocolVersion { get; set; }
    [ProtoMember(2)] public string ServerModVersion { get; set; }
    [ProtoMember(3)] public bool Accepted { get; set; }
    [ProtoMember(4)] public string Reason { get; set; }
    [ProtoMember(5)] public string[] EnabledFeatures { get; set; }
}
