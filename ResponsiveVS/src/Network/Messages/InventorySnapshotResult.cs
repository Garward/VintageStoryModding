using ProtoBuf;

namespace ResponsiveVS.Network.Messages;

[ProtoContract]
public sealed class InventorySnapshotResult
{
    [ProtoMember(1)] public string ClientSessionId { get; set; }
    [ProtoMember(2)] public string InventoryId { get; set; }
    [ProtoMember(3)] public SlotDelta[] Slots { get; set; }
    [ProtoMember(4)] public string Reason { get; set; }
}
