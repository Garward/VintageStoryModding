using Vintagestory.API.Common;
using Vintagestory.Common;

namespace ResponsiveVS.Diagnostics;

public static class InventoryDiagFormat
{
    public static string Slot(ItemSlot slot)
    {
        return Stack(slot?.Itemstack);
    }

    public static string Stack(ItemStack stack)
    {
        if (stack == null)
        {
            return "empty";
        }

        string code = stack.Collectible?.Code?.ToShortString() ?? "unknown";
        return stack.StackSize + "x" + code;
    }

    public static string PacketStack(Packet_ItemStack stack)
    {
        if (stack == null || stack.ItemClass < 0 || stack.ItemId == 0 || stack.StackSize <= 0)
        {
            return "empty";
        }

        return stack.StackSize + ":" + stack.ItemClass + ":" + stack.ItemId;
    }

    public static string ClientPacket(int packetId, Packet_Client packet)
    {
        if (packet == null)
        {
            return "packet=null";
        }

        switch (packetId)
        {
            case 7:
                Packet_ActivateInventorySlot activate = packet.ActivateInventorySlot;
                if (activate == null) return "activate=null";
                return $"activate target={activate.TargetInventoryId}[{activate.TargetSlot}] button={(EnumMouseButton)activate.MouseButton} mods={activate.Modifiers} priority={activate.Priority} last={activate.TargetLastChanged} dir={activate.Dir}";

            case 8:
                Packet_MoveItemstack move = packet.MoveItemstack;
                if (move == null) return "move=null";
                return $"move {move.SourceInventoryId}[{move.SourceSlot}]->{move.TargetInventoryId}[{move.TargetSlot}] qty={move.Quantity} button={(EnumMouseButton)move.MouseButton} mods={move.Modifiers} sourceLast={move.SourceLastChanged} targetLast={move.TargetLastChanged}";

            case 9:
                Packet_FlipItemstacks flip = packet.Flipitemstacks;
                if (flip == null) return "flip=null";
                return $"flip {flip.SourceInventoryId}[{flip.SourceSlot}]<->{flip.TargetInventoryId}[{flip.TargetSlot}] sourceLast={flip.SourceLastChanged} targetLast={flip.TargetLastChanged}";

            default:
                return "packetId=" + packetId;
        }
    }

    public static string ServerPacket(Packet_Server packet)
    {
        if (packet == null)
        {
            return "packet=null";
        }

        switch (packet.Id)
        {
            case 30:
                return $"contents inv={packet.InventoryContents?.InventoryId} count={packet.InventoryContents?.ItemstacksCount ?? 0}";
            case 31:
                return $"update inv={packet.InventoryUpdate?.InventoryId}[{packet.InventoryUpdate?.SlotId ?? -1}] stack={PacketStack(packet.InventoryUpdate?.ItemStack)}";
            case 32:
                return $"double inv1={packet.InventoryDoubleUpdate?.InventoryId1}[{packet.InventoryDoubleUpdate?.SlotId1 ?? -1}] stack1={PacketStack(packet.InventoryDoubleUpdate?.ItemStack1)} inv2={packet.InventoryDoubleUpdate?.InventoryId2}[{packet.InventoryDoubleUpdate?.SlotId2 ?? -1}] stack2={PacketStack(packet.InventoryDoubleUpdate?.ItemStack2)}";
            default:
                return "serverPacketId=" + packet.Id;
        }
    }
}
