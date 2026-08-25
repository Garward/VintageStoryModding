using System;

namespace ResponsiveVS.Transactions;

public readonly struct SlotKey : IEquatable<SlotKey>
{
    public SlotKey(string inventoryId, int slotId)
    {
        InventoryId = inventoryId ?? string.Empty;
        SlotId = slotId;
    }

    public string InventoryId { get; }
    public int SlotId { get; }

    public bool Equals(SlotKey other)
    {
        return SlotId == other.SlotId && string.Equals(InventoryId, other.InventoryId, StringComparison.Ordinal);
    }

    public override bool Equals(object obj)
    {
        return obj is SlotKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return ((InventoryId != null ? InventoryId.GetHashCode() : 0) * 397) ^ SlotId;
        }
    }

    public override string ToString()
    {
        return InventoryId + "[" + SlotId + "]";
    }
}
