using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VintageKinematics.Api;

namespace VintageKinematics.Blocks
{
    public class BlockCopperPipe : Block
    {
        private static readonly BlockFacing[] PipeFaces =
        {
            BlockFacing.NORTH,
            BlockFacing.EAST,
            BlockFacing.SOUTH,
            BlockFacing.WEST,
            BlockFacing.UP,
            BlockFacing.DOWN
        };

        private static readonly Dictionary<string, string> Opposites = new()
        {
            ["n"] = "s",
            ["e"] = "w",
            ["s"] = "n",
            ["w"] = "e",
            ["u"] = "d",
            ["d"] = "u"
        };

        private static readonly Dictionary<string, string> YRotated = new()
        {
            ["n"] = "e",
            ["e"] = "s",
            ["s"] = "w",
            ["w"] = "n",
            ["u"] = "u",
            ["d"] = "d"
        };

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
                mask = RotateMaskY(mask);
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
            foreach (BlockFacing face in PipeFaces)
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
            HashSet<string> faces = new();
            foreach (BlockFacing face in PipeFaces)
            {
                string faceCode = FaceCode(face);
                BlockPos neighborPos = pos.AddCopy(face);
                Block neighbor = world.BlockAccessor.GetBlock(neighborPos);
                if (IsCopperPipe(neighbor)
                    || BlockCopperPump.HasPipePort(neighbor, Opposites[faceCode])
                    || IsLiquidEndpoint(world, neighborPos, neighbor))
                {
                    faces.Add(faceCode);
                }
            }

            if (faces.Count == 0)
            {
                return NormalizeMask(current.Variant?["conn"]) ?? "ns";
            }

            if (faces.Count == 1)
            {
                string onlyFace = null;
                foreach (string face in faces) onlyFace = face;
                if (onlyFace != null) faces.Add(Opposites[onlyFace]);
            }

            return SortMask(faces);
        }

        internal static void UpdatePipeNeighbors(IWorldAccessor world, BlockPos pos)
        {
            if (world == null || pos == null) return;

            foreach (BlockFacing face in PipeFaces)
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
            string mask = block.Variant?["conn"];
            return !string.IsNullOrEmpty(mask) && mask.Contains(face);
        }

        internal static string FaceCode(BlockFacing face)
        {
            if (face == BlockFacing.NORTH) return "n";
            if (face == BlockFacing.EAST) return "e";
            if (face == BlockFacing.SOUTH) return "s";
            if (face == BlockFacing.WEST) return "w";
            if (face == BlockFacing.UP) return "u";
            return "d";
        }

        private static string RotateMaskY(string mask)
        {
            HashSet<string> rotated = new();
            foreach (char ch in mask)
            {
                string face = ch.ToString();
                if (YRotated.TryGetValue(face, out string next))
                {
                    rotated.Add(next);
                }
            }
            return SortMask(rotated);
        }

        private static string NormalizeMask(string mask)
        {
            if (string.IsNullOrEmpty(mask)) return null;
            HashSet<string> faces = new();
            foreach (char ch in mask)
            {
                string face = ch.ToString();
                if (Opposites.ContainsKey(face))
                {
                    faces.Add(face);
                }
            }
            return faces.Count == 0 ? null : SortMask(faces);
        }

        private static string SortMask(HashSet<string> faces)
        {
            Span<char> order = stackalloc[] { 'n', 'e', 's', 'w', 'u', 'd' };
            char[] result = new char[faces.Count];
            int index = 0;
            foreach (char face in order)
            {
                if (faces.Contains(face.ToString()))
                {
                    result[index++] = face;
                }
            }
            return new string(result);
        }
    }
}
