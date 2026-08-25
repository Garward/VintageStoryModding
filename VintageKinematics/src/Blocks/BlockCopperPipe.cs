using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VintageKinematics.Api;
using VintageKinematics.Connections;

namespace VintageKinematics.Blocks
{
    public class BlockCopperPipe : Block
    {
        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemStack, BlockSelection blockSel, ref string failureCode)
        {
            string initial = InitialMask(blockSel);
            Block variant = world.GetBlock(CodeWithVariant("conn", initial));
            if (variant != null && variant != this)
            {
                return variant.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);
            }

            return base.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);
        }

        public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack byItemStack = null)
        {
            base.OnBlockPlaced(world, blockPos, byItemStack);
            UpdatePipeAndNeighbors(world, blockPos);
        }

        public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
        {
            base.OnNeighbourBlockChange(world, pos, neibpos);
            UpdatePipeAt(world, pos);
        }

        public override AssetLocation GetRotatedBlockCode(int angle)
        {
            string mask = Variant?["conn"];
            if (string.IsNullOrEmpty(mask)) return base.GetRotatedBlockCode(angle);

            int turns = ((angle % 360) + 360) % 360 / 90;
            for (int i = 0; i < turns; i++)
            {
                mask = FaceConnectionMask.RotateY(mask);
            }

            return CodeWithVariant("conn", mask);
        }

        private static string InitialMask(BlockSelection blockSel)
        {
            return blockSel?.Face?.Axis switch
            {
                EnumAxis.X => "ew",
                EnumAxis.Y => "ud",
                _ => "ns"
            };
        }

        private static void UpdatePipeAndNeighbors(IWorldAccessor world, BlockPos pos)
        {
            UpdatePipeAt(world, pos);
            foreach (BlockFacing face in FaceConnectionMask.Faces)
            {
                BlockPos neighbor = pos.AddCopy(face);
                UpdatePipeAt(world, neighbor);
            }
        }

        internal static void UpdatePipeAt(IWorldAccessor world, BlockPos pos)
        {
            Block block = world.BlockAccessor.GetBlock(pos);
            if (!IsCopperPipe(block)) return;

            string desiredMask = DesiredMask(world, pos, block);
            if (string.IsNullOrEmpty(desiredMask) || block.Variant?["conn"] == desiredMask) return;

            Block variant = world.GetBlock(block.CodeWithVariant("conn", desiredMask));
            if (variant == null || variant.Id == block.Id) return;

            world.BlockAccessor.SetBlock(variant.BlockId, pos);
            world.BlockAccessor.MarkBlockDirty(pos);
        }

        private static string DesiredMask(IWorldAccessor world, BlockPos pos, Block current)
        {
            string mask = WorldFaceConnectionScanner.Scan(
                world,
                pos,
                (face, neighborPos, neighbor) => IsCopperPipe(neighbor)
                    || BlockCopperPump.HasPipePort(
                        neighbor,
                        FaceConnectionMask.Opposite(FaceConnectionMask.Code(face)))
                    || IsLiquidEndpoint(world, neighborPos, neighbor));

            if (string.IsNullOrEmpty(mask))
            {
                return FaceConnectionMask.Normalize(current.Variant?["conn"]) ?? "ns";
            }

            if (mask.Length == 1)
            {
                mask = FaceConnectionMask.Add(
                    mask,
                    FaceConnectionMask.Opposite(mask));
            }

            return mask;
        }

        internal static void UpdatePipeNeighbors(IWorldAccessor world, BlockPos pos)
        {
            if (world == null || pos == null) return;

            foreach (BlockFacing face in FaceConnectionMask.Faces)
            {
                UpdatePipeAt(world, pos.AddCopy(face));
            }
        }

        internal static bool IsCopperPipe(Block block)
        {
            return block?.Code?.Domain == "vintagekinematics"
                && block.Code.Path.StartsWith("copperpipe-", StringComparison.Ordinal);
        }

        private static bool IsLiquidEndpoint(IWorldAccessor world, BlockPos pos, Block block)
        {
            if (block is ILiquidSource || block is ILiquidSink || block is ILiquidInterface) return true;
            if (block?.GetCollectibleInterface<ILiquidSource>() != null
                || block?.GetCollectibleInterface<ILiquidSink>() != null
                || block?.GetCollectibleInterface<ILiquidInterface>() != null)
            {
                return true;
            }

            Block controllerBlock = MultiblockHelper.GetMultiblockAwareBE(world, pos)?.Block;
            if (controllerBlock != null && controllerBlock != block)
            {
                if (controllerBlock is ILiquidSource || controllerBlock is ILiquidSink || controllerBlock is ILiquidInterface) return true;
                if (controllerBlock.GetCollectibleInterface<ILiquidSource>() != null
                    || controllerBlock.GetCollectibleInterface<ILiquidSink>() != null
                    || controllerBlock.GetCollectibleInterface<ILiquidInterface>() != null)
                {
                    return true;
                }
            }

            return HasWorldLiquidFillProps(world, pos);
        }

        private static bool HasWorldLiquidFillProps(IWorldAccessor world, BlockPos pos)
        {
            Block block = world.BlockAccessor.GetBlock(pos, BlockLayersAccess.FluidOrSolid);
            WaterTightContainableProps props = block?.Attributes?["waterTightContainerProps"]?.AsObject<WaterTightContainableProps>();
            return props?.WhenFilled != null && props.Containable;
        }

        internal static bool HasPipeConnection(Block block, string face)
        {
            if (!IsCopperPipe(block) || string.IsNullOrEmpty(face)) return false;
            return FaceConnectionMask.Contains(block.Variant?["conn"], face);
        }

        internal static string FaceCode(BlockFacing face)
        {
            return FaceConnectionMask.Code(face);
        }
    }
}
