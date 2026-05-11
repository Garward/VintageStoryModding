using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.BlockEntities;

namespace VintageKinematics.Blocks
{
    /// <summary>
    /// Two horizontal axis variants: <c>basin-x</c> (input on +X, side-output on −X) and <c>basin-z</c>
    /// (input on +Z, side-output on −Z). The bottom face is always a passive output. Shift-right-click
    /// empty-handed opens the GUI; plain right-click on a face inserts (with item) or extracts (empty)
    /// from that face's slot.
    /// </summary>
    public class BlockCrusherBasin : BlockAxisOriented
    {
        public override string GetPlacementVariantAxis(IWorldAccessor world, IPlayer byPlayer, ItemStack itemStack, BlockSelection blockSel)
        {
            if (byPlayer?.Entity != null)
            {
                float yaw = byPlayer.Entity.Pos.Yaw;
                // Yaw 0 = south (+Z); pi/2 = west (-X). Pick axis perpendicular to the player so the
                // input face naturally points at them.
                double rad = yaw % (Math.PI * 2);
                if (rad < 0) rad += Math.PI * 2;
                bool eastWestLook = (rad > Math.PI / 4 && rad < 3 * Math.PI / 4)
                                    || (rad > 5 * Math.PI / 4 && rad < 7 * Math.PI / 4);
                return eastWestLook ? "x" : "z";
            }
            return base.GetPlacementVariantAxis(world, byPlayer, itemStack, blockSel);
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            BECrusherBasin be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BECrusherBasin;
            if (be == null) return base.OnBlockInteractStart(world, byPlayer, blockSel);

            ItemSlot active = byPlayer.InventoryManager.ActiveHotbarSlot;
            bool empty = active == null || active.Empty;
            bool sneak = byPlayer.Entity.Controls.Sneak;

            // Sneak + empty hand → GUI. Sneak + holding anything → fall through to vanilla place.
            if (sneak)
            {
                if (!empty) return false;
                return be.OnPlayerRightClick(byPlayer, blockSel);
            }

            FaceKind kind = ClassifyFace(blockSel.Face);
            if (kind == FaceKind.None) return base.OnBlockInteractStart(world, byPlayer, blockSel);

            if (empty)
            {
                // Pull from the targeted face: input face yields the input slot, output faces
                // yield the first non-empty slot in the shared 9-cell output buffer.
                ItemSlot src = kind == FaceKind.Input
                    ? be.Inventory[BECrusherBasin.SlotInput]
                    : FindNonEmptyOutput(be);
                if (src == null || src.Empty) return true;
                if (world.Side == EnumAppSide.Server)
                {
                    if (!byPlayer.InventoryManager.TryGiveItemstack(src.Itemstack, true))
                    {
                        world.SpawnItemEntity(src.Itemstack, blockSel.Position.ToVec3d().Add(0.5, 0.7, 0.5));
                    }
                    src.Itemstack = null;
                    src.MarkDirty();
                }
                return true;
            }

            // Holding an item → only the input face accepts insertion.
            if (kind != FaceKind.Input) return true;

            ItemSlot inv = be.Inventory[BECrusherBasin.SlotInput];
            if (world.Side == EnumAppSide.Server)
            {
                int moved = active.TryPutInto(world, inv, active.StackSize);
                if (moved > 0) inv.MarkDirty();
            }
            return true;
        }

        private enum FaceKind { None, Input, Output }

        private FaceKind ClassifyFace(BlockFacing face)
        {
            if (face == BlockFacing.DOWN) return FaceKind.Output;

            string axis = Variant["axis"] ?? "x";
            if (axis == "x")
            {
                if (face == BlockFacing.EAST) return FaceKind.Input;
                if (face == BlockFacing.WEST) return FaceKind.Output;
            }
            else
            {
                if (face == BlockFacing.SOUTH) return FaceKind.Input;
                if (face == BlockFacing.NORTH) return FaceKind.Output;
            }
            return FaceKind.None;
        }

        private static ItemSlot FindNonEmptyOutput(BECrusherBasin be)
        {
            for (int i = BECrusherBasin.SlotOutputFirst; i <= BECrusherBasin.SlotOutputLast; i++)
            {
                ItemSlot s = be.Inventory[i];
                if (s != null && !s.Empty) return s;
            }
            return null;
        }
    }
}
