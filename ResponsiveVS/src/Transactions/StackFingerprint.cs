using Vintagestory.API.Common;

namespace ResponsiveVS.Transactions;

public readonly struct StackFingerprint
{
    public StackFingerprint(int itemClass, int itemId, int stackSize, int attributesHash)
    {
        ItemClass = itemClass;
        ItemId = itemId;
        StackSize = stackSize;
        AttributesHash = attributesHash;
    }

    public int ItemClass { get; }
    public int ItemId { get; }
    public int StackSize { get; }
    public int AttributesHash { get; }

    public static StackFingerprint FromSlot(ItemSlot slot)
    {
        ItemStack stack = slot?.Itemstack;
        if (stack == null)
        {
            return default;
        }

        int attrHash = stack.Attributes == null ? 0 : stack.Attributes.GetHashCode();
        return new StackFingerprint((int)stack.Class, stack.Id, stack.StackSize, attrHash);
    }

    public override string ToString()
    {
        if (ItemId == 0 || StackSize == 0) return "empty";
        return ItemClass + ":" + ItemId + ":" + StackSize + ":" + AttributesHash;
    }
}
