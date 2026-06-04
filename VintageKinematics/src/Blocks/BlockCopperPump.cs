using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.BlockEntities;

namespace VintageKinematics.Blocks
{
    public class BlockCopperPump : Block
    {
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
            string direction = PlacementDirection(blockSel);
            Block variant = world.GetBlock(CodeWithVariant("direction", direction));
            if (variant != null && variant != this)
            {
                return variant.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);
            }

            return base.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);
        }

        public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack byItemStack = null)
        {
            base.OnBlockPlaced(world, blockPos, byItemStack);
            BlockCopperPipe.UpdatePipeNeighbors(world, blockPos);
        }

        public override AssetLocation GetRotatedBlockCode(int angle)
        {
            string direction = Variant?["direction"];
            if (string.IsNullOrEmpty(direction)) return base.GetRotatedBlockCode(angle);

            int turns = ((angle % 360) + 360) % 360 / 90;
            for (int i = 0; i < turns; i++)
            {
                if (YRotated.TryGetValue(direction, out string next))
                {
                    direction = next;
                }
            }

            return CodeWithVariant("direction", direction);
        }

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
        {
            int quantity = Math.Max(1, (int)Math.Round(dropQuantityMultiplier));
            return new[] { CanonicalStack(world, quantity) ?? new ItemStack(this, quantity) };
        }

        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            return CanonicalStack(world, 1) ?? base.OnPickBlock(world, pos);
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (KineticInteractionHelper.ShouldDeferToHeldWrench(byPlayer)) return false;
            BECopperPump be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BECopperPump;
            if (be == null) return base.OnBlockInteractStart(world, byPlayer, blockSel);
            return be.OnPlayerRightClick(byPlayer, blockSel);
        }

        internal static bool HasPipePort(Block block, string face)
        {
            if (!IsCopperPump(block) || string.IsNullOrEmpty(face)) return false;

            string direction = block.Variant?["direction"];
            if (string.IsNullOrEmpty(direction)) return false;

            return face == direction || (Opposites.TryGetValue(direction, out string opposite) && face == opposite);
        }

        private ItemStack CanonicalStack(IWorldAccessor world, int quantity)
        {
            Block pump = world?.GetBlock(CodeWithVariant("direction", "s"));
            return pump == null ? null : new ItemStack(pump, quantity);
        }

        private static bool IsCopperPump(Block block)
        {
            return block?.Code?.Domain == "vintagekinematics"
                && block.Code.Path.StartsWith("copperpump-", StringComparison.Ordinal);
        }

        private static string PlacementDirection(BlockSelection blockSel)
        {
            if (blockSel?.Face == BlockFacing.NORTH) return "n";
            if (blockSel?.Face == BlockFacing.EAST) return "e";
            if (blockSel?.Face == BlockFacing.SOUTH) return "s";
            if (blockSel?.Face == BlockFacing.WEST) return "w";
            if (blockSel?.Face == BlockFacing.UP) return "u";
            if (blockSel?.Face == BlockFacing.DOWN) return "d";
            return "s";
        }
    }
}
