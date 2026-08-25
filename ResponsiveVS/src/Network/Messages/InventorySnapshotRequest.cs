using ProtoBuf;

namespace ResponsiveVS.Network.Messages;

[ProtoContract]
public sealed class InventorySnapshotRequest
{
    [ProtoMember(1)] public string ClientSessionId { get; set; }
    [ProtoMember(2)] public string[] InventoryIds { get; set; }
    [ProtoMember(3)] public string Reason { get; set; }
}
