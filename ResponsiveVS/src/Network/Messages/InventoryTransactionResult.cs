using ProtoBuf;

namespace ResponsiveVS.Network.Messages;

[ProtoContract]
public sealed class InventoryTransactionResult
{
    [ProtoMember(1)] public long TransactionId { get; set; }
    [ProtoMember(2)] public string ClientSessionId { get; set; }
    [ProtoMember(3)] public bool Accepted { get; set; }
    [ProtoMember(4)] public int RejectReason { get; set; }
    [ProtoMember(5)] public string Message { get; set; }
    [ProtoMember(6)] public SlotDelta[] Deltas { get; set; }
    [ProtoMember(7)] public bool RequiresSnapshot { get; set; }
}
