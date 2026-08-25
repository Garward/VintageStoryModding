using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Common;

namespace ResponsiveVS.Diagnostics;

public static class WorldInteractionDiagFormat
{
    public static string ClientSend(
        int mouseButton,
        BlockSelection blockSel,
        EntitySelection entitySel,
        EnumHandInteract useType,
        EnumHandInteractNw state,
        bool firstEvent,
        EnumItemUseCancelReason cancelReason,
        EntityAgent entity)
    {
        return string.Format(
            "button={0} state={1} use={2} first={3} cancel={4} using={5} slot={6} {7} {8}",
            mouseButton,
            state,
            useType,
            firstEvent,
            cancelReason,
            entity?.Controls?.UsingCount ?? -1,
            InventoryDiagFormat.Slot(entity?.RightHandItemSlot),
            BlockSelection(blockSel),
            EntitySelection(entitySel));
    }

    public static string HandPacket(Packet_ClientHandInteraction packet)
    {
        if (packet == null)
        {
            return "hand=null";
        }

        return string.Format(
            "button={0} state={1} use={2} first={3} cancel={4} using={5} slot={6}:{7}[{8}] block={9}/{10}/{11} face={12} hit={13}/{14}/{15} entity={16} box={17}:{18}",
            packet.MouseButton,
            SafeEnum<EnumHandInteractNw>(packet.EnumHandInteract),
            SafeEnum<EnumHandInteract>(packet.UseType),
            packet.FirstEvent > 0,
            SafeEnum<EnumItemUseCancelReason>(packet.CancelReason),
            packet.UsingCount,
            packet.InventoryId ?? "hotbar",
            packet.SlotId,
            packet.SlotId,
            packet.X,
            packet.Y,
            packet.Z,
            SafeFace(packet.OnBlockFace),
            packet.HitX,
            packet.HitY,
            packet.HitZ,
            packet.OnEntityId,
            packet.SelectionBoxIndex,
            packet.SelectionBoxId ?? "");
    }

    public static string HeldUse(
        string phase,
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        EnumHandInteract useType,
        float seconds = 0,
        EnumItemUseCancelReason? cancelReason = null)
    {
        return string.Format(
            "{0} side={1} player={2} use={3} handUse={4} seconds={5:0.000} using={6} cancel={7} slot={8} {9} {10}",
            phase,
            byEntity?.Api?.Side,
            PlayerName(byEntity),
            useType,
            byEntity?.Controls?.HandUse,
            seconds,
            byEntity?.Controls?.UsingCount ?? -1,
            cancelReason?.ToString() ?? "none",
            InventoryDiagFormat.Slot(slot),
            BlockSelection(blockSel),
            EntitySelection(entitySel));
    }

    public static string BlockUse(
        string phase,
        IWorldAccessor world,
        IPlayer player,
        Block block,
        BlockSelection blockSel,
        float seconds = 0,
        EnumItemUseCancelReason? cancelReason = null)
    {
        return string.Format(
            "{0} side={1} player={2} block={3} seconds={4:0.000} cancel={5} {6}",
            phase,
            world?.Side,
            player?.PlayerName,
            block?.Code?.ToShortString() ?? "unknown",
            seconds,
            cancelReason?.ToString() ?? "none",
            BlockSelection(blockSel));
    }

    public static string BlockSelection(BlockSelection blockSel)
    {
        if (blockSel == null)
        {
            return "block=none";
        }

        BlockPos pos = blockSel.Position;
        Vec3d hit = blockSel.HitPosition;
        return string.Format(
            "block={0}/{1}/{2} face={3} hit={4:0.000}/{5:0.000}/{6:0.000} box={7}:{8}",
            pos?.X ?? 0,
            pos?.Y ?? 0,
            pos?.Z ?? 0,
            blockSel.Face?.Code ?? "none",
            hit?.X ?? 0,
            hit?.Y ?? 0,
            hit?.Z ?? 0,
            blockSel.SelectionBoxIndex,
            blockSel.SelectionBoxId ?? "");
    }

    public static string EntitySelection(EntitySelection entitySel)
    {
        if (entitySel == null)
        {
            return "entity=none";
        }

        Vec3d hit = entitySel.HitPosition;
        return string.Format(
            "entity={0} face={1} hit={2:0.000}/{3:0.000}/{4:0.000} box={5}",
            entitySel.Entity?.EntityId ?? 0,
            entitySel.Face?.Code ?? "none",
            hit?.X ?? 0,
            hit?.Y ?? 0,
            hit?.Z ?? 0,
            entitySel.SelectionBoxIndex);
    }

    private static string PlayerName(EntityAgent entity)
    {
        return (entity as EntityPlayer)?.Player?.PlayerName ?? entity?.EntityId.ToString() ?? "unknown";
    }

    private static string SafeFace(int faceIndex)
    {
        return faceIndex >= 0 && faceIndex < BlockFacing.ALLFACES.Length ? BlockFacing.ALLFACES[faceIndex].Code : faceIndex.ToString();
    }

    private static string SafeEnum<T>(int value) where T : struct, Enum
    {
        return Enum.IsDefined(typeof(T), value) ? ((T)(object)value).ToString() : value.ToString();
    }
}
