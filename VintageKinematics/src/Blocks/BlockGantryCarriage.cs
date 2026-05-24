using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.BlockEntities;

namespace VintageKinematics.Blocks
{
    public class BlockGantryCarriage : Block, IPlacementPreviewProvider
    {
        private WorldInteraction[] interactions;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            if (api.Side != EnumAppSide.Client) return;

            interactions = new[]
            {
                new WorldInteraction
                {
                    ActionLangCode = "vintagekinematics:blockhelp-gantrycarriage-toggle",
                    MouseButton = EnumMouseButton.Right
                },
                new WorldInteraction
                {
                    ActionLangCode = "vintagekinematics:blockhelp-gantrycarriage-clear",
                    MouseButton = EnumMouseButton.Right,
                    HotKeyCode = "sneak"
                }
            };
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            return interactions ?? base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
        }

        public bool TryResolvePlacementPreview(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, out BlockPos targetPos, out Block variant)
        {
            targetPos = null;
            variant = null;
            if (blockSel?.Position == null) return false;

            BlockPos shaftPos = ResolveSelectedShaftPos(blockSel);
            Block targetBlock = world.BlockAccessor.GetBlock(shaftPos);
            if (!TryGetGantryAxis(targetBlock, out string axis)) return false;

            targetPos = ResolveAttachmentPos(blockSel, shaftPos, axis);
            if (targetPos == null || targetPos.Equals(shaftPos)) return false;

            string side = ResolveAttachmentSide(shaftPos, targetPos, axis);
            if (side == null) return false;

            variant = world.GetBlock(CodeWithVariants(new[] { "axis", "side" }, new[] { axis, side })) ?? this;
            return true;
        }

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemStack, BlockSelection blockSel, ref string failureCode)
        {
            if (!TryResolvePlacementPreview(world, byPlayer, blockSel, out BlockPos targetPos, out Block variant))
            {
                failureCode = "requiregantryshaft";
                return false;
            }

            BlockPos shaftPos = ResolveSelectedShaftPos(blockSel);
            Block existingTarget = world.BlockAccessor.GetBlock(targetPos);
            if (existingTarget != null && !existingTarget.IsReplacableBy(variant))
            {
                failureCode = "occupied";
                return false;
            }

            if (byPlayer != null && (!world.Claims.TryAccess(byPlayer, targetPos, EnumBlockAccessFlags.BuildOrBreak) || !world.Claims.TryAccess(byPlayer, shaftPos, EnumBlockAccessFlags.Use)))
            {
                failureCode = "claimed";
                return false;
            }

            world.BlockAccessor.SetBlock(variant.BlockId, targetPos, itemStack);
            if (world.BlockAccessor.GetBlockEntity(targetPos) is BEGantryCarriage be)
            {
                be.MarkPlacedOnGantry(shaftPos);
            }
            world.BlockAccessor.MarkBlockDirty(targetPos);
            return true;
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (blockSel == null) return false;
            if (world.Side == EnumAppSide.Client) return true;

            return world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEGantryCarriage be
                && be.OnPlayerRightClick(byPlayer);
        }

        private static bool TryGetGantryAxis(Block block, out string axis)
        {
            axis = null;
            if (block?.Code == null) return false;
            if (block.Code.Domain != "vintagekinematics" || block.Code.FirstCodePart() != "gantryshaft") return false;

            axis = block.Variant["axis"] ?? "y";
            return axis == "x" || axis == "y" || axis == "z";
        }

        private static BlockPos ResolveSelectedShaftPos(BlockSelection blockSel)
        {
            return blockSel.DidOffset && blockSel.Face != null
                ? blockSel.Position.AddCopy(blockSel.Face.Opposite)
                : blockSel.Position.Copy();
        }

        private static BlockPos ResolveAttachmentPos(BlockSelection blockSel, BlockPos shaftPos, string axis)
        {
            if (axis == "x" || axis == "z") return shaftPos.UpCopy();
            if (blockSel.Face?.IsHorizontal != true) return null;
            if (blockSel.DidOffset) return blockSel.Position.Copy();
            return blockSel.Position.AddCopy(blockSel.Face);
        }

        private static string ResolveAttachmentSide(BlockPos shaftPos, BlockPos targetPos, string axis)
        {
            int dx = targetPos.X - shaftPos.X;
            int dy = targetPos.InternalY - shaftPos.InternalY;
            int dz = targetPos.Z - shaftPos.Z;

            if (axis == "x" || axis == "z")
            {
                return dx == 0 && dy == 1 && dz == 0 ? "u" : null;
            }

            if (dy != 0) return null;
            if (dx == 0 && dz == -1) return "n";
            if (dx == 1 && dz == 0) return "e";
            if (dx == 0 && dz == 1) return "s";
            if (dx == -1 && dz == 0) return "w";
            return null;
        }
    }
}
