using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.BlockEntities;

namespace VintageKinematics.Blocks
{
    /// <summary>
    /// Side-oriented crusher basin. New variants use <c>side</c> to encode the placement facing:
    /// left face is input, right face is output. Legacy x/z variants are kept as aliases.
    /// </summary>
    public class BlockCrusherBasin : Block, IPlacementPreviewProvider
    {
        public bool TryResolvePlacementPreview(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, out BlockPos targetPos, out Block variant)
        {
            targetPos = null;
            variant = null;
            if (blockSel?.Face == null) return false;

            targetPos = PlacementPreview.DefaultTargetPos(world, blockSel, this);
            string desired = PlacementSide(byPlayer);
            if (desired == null)
            {
                variant = this;
                return true;
            }

            variant = world.GetBlock(CodeWithVariant("side", desired)) ?? this;
            return true;
        }

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemStack, BlockSelection blockSel, ref string failureCode)
        {
            if (!TryResolvePlacementPreview(world, byPlayer, blockSel, out _, out Block variant) || variant == this)
                return base.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);
            return variant.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);
        }

        private static string PlacementSide(IPlayer byPlayer)
        {
            if (byPlayer?.Entity == null) return null;
            BlockFacing facing = BlockFacing.HorizontalFromYaw(byPlayer.Entity.Pos.Yaw);
            if (facing == BlockFacing.NORTH) return "n";
            if (facing == BlockFacing.EAST) return "e";
            if (facing == BlockFacing.SOUTH) return "s";
            if (facing == BlockFacing.WEST) return "w";
            return null;
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

            BlockFacing facing = FacingFromVariant(Variant?["side"]);
            if (face == LeftOf(facing)) return FaceKind.Input;
            if (face == RightOf(facing)) return FaceKind.Output;
            return FaceKind.None;
        }

        private static BlockFacing FacingFromVariant(string side)
        {
            switch (side)
            {
                case "n": return BlockFacing.NORTH;
                case "e": return BlockFacing.EAST;
                case "w": return BlockFacing.WEST;
                case "z": return BlockFacing.WEST;
                case "s":
                case "x":
                default: return BlockFacing.SOUTH;
            }
        }

        private static BlockFacing LeftOf(BlockFacing facing)
        {
            if (facing == BlockFacing.NORTH) return BlockFacing.WEST;
            if (facing == BlockFacing.EAST) return BlockFacing.NORTH;
            if (facing == BlockFacing.SOUTH) return BlockFacing.EAST;
            if (facing == BlockFacing.WEST) return BlockFacing.SOUTH;
            return BlockFacing.EAST;
        }

        private static BlockFacing RightOf(BlockFacing facing)
        {
            if (facing == BlockFacing.NORTH) return BlockFacing.EAST;
            if (facing == BlockFacing.EAST) return BlockFacing.SOUTH;
            if (facing == BlockFacing.SOUTH) return BlockFacing.WEST;
            if (facing == BlockFacing.WEST) return BlockFacing.NORTH;
            return BlockFacing.WEST;
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
