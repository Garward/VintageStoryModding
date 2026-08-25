using ProtoBuf;

namespace ResponsiveVS.Network.Messages;

[ProtoContract]
public sealed class SlotDelta
{
    [ProtoMember(1)] public string InventoryId { get; set; }
    [ProtoMember(2)] public int SlotId { get; set; }
    [ProtoMember(3)] public int ItemClass { get; set; }
    [ProtoMember(4)] public int ItemId { get; set; }
    [ProtoMember(5)] public int StackSize { get; set; }
    [ProtoMember(6)] public byte[] Attributes { get; set; }
    [ProtoMember(7)] public long LastChanged { get; set; }
}
