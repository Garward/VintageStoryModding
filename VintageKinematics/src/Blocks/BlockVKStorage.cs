using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VintageKinematics.Api;
using VintageKinematics.BlockEntities;

namespace VintageKinematics.Blocks
{
    public class BlockVKStorage : Block, IPlacementPreviewProvider, IMultiBlockColSelBoxes
    {
        private WorldInteraction[] bulkCrateInteractions;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            if (Code?.Path?.StartsWith("bulkcrate-") == true)
            {
                PlacedPriorityInteract = true;
            }
        }

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

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (blockSel == null) return false;
            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEBulkCrate crate)
            {
                return crate.OnPlayerRightClick(byPlayer, blockSel);
            }

            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            WorldInteraction[] baseInteractions = base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
            if (Code?.Path?.StartsWith("bulkcrate-") != true) return baseInteractions;

            bulkCrateInteractions ??= new[]
            {
                new WorldInteraction { ActionLangCode = "blockhelp-crate-add", MouseButton = EnumMouseButton.Right, HotKeyCode = "shift" },
                new WorldInteraction { ActionLangCode = "blockhelp-crate-addall", MouseButton = EnumMouseButton.Right, HotKeyCodes = new[] { "shift", "ctrl" } },
                new WorldInteraction { ActionLangCode = "blockhelp-crate-remove", MouseButton = EnumMouseButton.Right },
                new WorldInteraction { ActionLangCode = "blockhelp-crate-removeall", MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" }
            };

            return baseInteractions == null
                ? bulkCrateInteractions
                : baseInteractions.Concat(bulkCrateInteractions).ToArray();
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
    }
}
