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

            int faceSlot = SlotForFace(be, blockSel.Face);
            if (faceSlot < 0) return base.OnBlockInteractStart(world, byPlayer, blockSel);

            ItemSlot inv = be.Inventory[faceSlot];

            if (empty)
            {
                // Pull from the targeted slot
                if (inv.Empty) return true;
                if (world.Side == EnumAppSide.Server)
                {
                    if (!byPlayer.InventoryManager.TryGiveItemstack(inv.Itemstack, true))
                    {
                        world.SpawnItemEntity(inv.Itemstack, blockSel.Position.ToVec3d().Add(0.5, 0.7, 0.5));
                    }
                    inv.Itemstack = null;
                    inv.MarkDirty();
                }
                return true;
            }

            // Holding an item → only the input face accepts insertion.
            if (faceSlot != BECrusherBasin.SlotInput) return true;

            if (world.Side == EnumAppSide.Server)
            {
                int moved = active.TryPutInto(world, inv, active.StackSize);
                if (moved > 0) inv.MarkDirty();
            }
            return true;
        }

        /// <summary>Maps a block face to one of the basin's slots, or -1 if no slot for that face.</summary>
        private int SlotForFace(BECrusherBasin be, BlockFacing face)
        {
            if (face == BlockFacing.DOWN) return BECrusherBasin.SlotBottomOutput;

            string axis = Variant["axis"] ?? "x";
            if (axis == "x")
            {
                if (face == BlockFacing.EAST) return BECrusherBasin.SlotInput;
                if (face == BlockFacing.WEST) return BECrusherBasin.SlotSideOutput;
            }
            else
            {
                if (face == BlockFacing.SOUTH) return BECrusherBasin.SlotInput;
                if (face == BlockFacing.NORTH) return BECrusherBasin.SlotSideOutput;
            }
            return -1;
        }
    }
}
