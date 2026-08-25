using ProtoBuf;

namespace ResponsiveVS.Network.Messages;

[ProtoContract]
public sealed class InventoryTransactionRequest
{
    [ProtoMember(1)] public long TransactionId { get; set; }
    [ProtoMember(2)] public string ClientSessionId { get; set; }
    [ProtoMember(3)] public int OperationKind { get; set; }
    [ProtoMember(4)] public string SourceInventoryId { get; set; }
    [ProtoMember(5)] public int SourceSlotId { get; set; }
    [ProtoMember(6)] public long SourceLastChanged { get; set; }
    [ProtoMember(7)] public string TargetInventoryId { get; set; }
    [ProtoMember(8)] public int TargetSlotId { get; set; }
    [ProtoMember(9)] public long TargetLastChanged { get; set; }
    [ProtoMember(10)] public int MouseButton { get; set; }
    [ProtoMember(11)] public int Modifiers { get; set; }
    [ProtoMember(12)] public int RequestedQuantity { get; set; }
    [ProtoMember(13)] public int[] DragSlotIds { get; set; }
    [ProtoMember(14)] public string DragInventoryId { get; set; }
}
