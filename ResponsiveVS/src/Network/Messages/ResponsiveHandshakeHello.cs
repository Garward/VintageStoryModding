using ProtoBuf;

namespace ResponsiveVS.Network.Messages;

[ProtoContract]
public sealed class ResponsiveHandshakeHello
{
    [ProtoMember(1)] public int ProtocolVersion { get; set; }
    [ProtoMember(2)] public string ClientModVersion { get; set; }
    [ProtoMember(3)] public string ClientSessionId { get; set; }
    [ProtoMember(4)] public string[] SupportedFeatures { get; set; }
}
