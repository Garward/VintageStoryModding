using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VintageKinematics.Api;

namespace VintageKinematics.Blocks
{
    public class BlockVKStorage : BlockGenericTypedContainer, IPlacementPreviewProvider, IMultiBlockColSelBoxes
    {
        private static string FacingPlayer(IPlayer byPlayer)
        {
            if (byPlayer?.Entity == null) return "s";
            BlockFacing facing = BlockFacing.HorizontalFromYaw(byPlayer.Entity.Pos.Yaw);
            if (facing == BlockFacing.NORTH) return "s";
            if (facing == BlockFacing.EAST) return "e";
            if (facing == BlockFacing.SOUTH) return "n";
            if (facing == BlockFacing.WEST) return "w";
            return "s";
        }

        public bool TryResolvePlacementPreview(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, out BlockPos targetPos, out Block variant)
        {
            targetPos = null;
            variant = null;
            if (blockSel?.Face == null) return false;

            targetPos = PlacementPreview.DefaultTargetPos(world, blockSel, this);
            variant = world.GetBlock(CodeWithVariant("side", FacingPlayer(byPlayer))) ?? this;
            return true;
        }

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemStack, BlockSelection blockSel, ref string failureCode)
        {
            if (!TryResolvePlacementPreview(world, byPlayer, blockSel, out _, out Block variant) || variant == this)
            {
                return base.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);
            }

            return variant.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);
        }

        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            return CreateStorageStack(world, pos);
        }

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            return new[] { CreateStorageStack(world, pos) };
        }

        public Cuboidf[] MBGetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos, Vec3i offset)
        {
            return ShiftMultiblockBoxes(base.GetCollisionBoxes(blockAccessor, pos.AddCopy(offset)), offset);
        }

        public Cuboidf[] MBGetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos, Vec3i offset)
        {
            return ShiftMultiblockBoxes(base.GetSelectionBoxes(blockAccessor, pos.AddCopy(offset)), offset);
        }

        private static Cuboidf[] ShiftMultiblockBoxes(Cuboidf[] boxes, Vec3i offset)
        {
            if (boxes == null) return null;
            Cuboidf[] shifted = new Cuboidf[boxes.Length];
            for (int i = 0; i < boxes.Length; i++)
            {
                shifted[i] = boxes[i]?.OffsetCopy(offset.X, offset.Y, offset.Z);
            }
            return shifted;
        }

        private ItemStack CreateStorageStack(IWorldAccessor world, BlockPos pos)
        {
            Block itemBlock = world.GetBlock(CodeWithVariant("side", "s")) ?? this;
            ItemStack stack = new ItemStack(itemBlock);
            string type = (world.BlockAccessor.GetBlockEntity(pos) as BlockEntityGenericTypedContainer)?.type
                ?? Attributes?["defaultType"]?.AsString("normal-generic")
                ?? "normal-generic";
            stack.Attributes.SetString("type", type);
            return stack;
        }
    }
}
