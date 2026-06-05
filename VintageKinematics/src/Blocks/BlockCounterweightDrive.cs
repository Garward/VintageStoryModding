using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VintageKinematics.Api;
using VintageKinematics.BlockEntities;

namespace VintageKinematics.Blocks
{
    public class BlockCounterweightDrive : Block, IPlacementPreviewProvider, IMultiBlockColSelBoxes
    {
        private WorldInteraction[] interactions;
        private static readonly Cuboidf[] EmptyBoxes = Array.Empty<Cuboidf>();
        private static readonly Cuboidf[] BottomBaseBoxes =
        {
            new Cuboidf(0f, 0f, 0f, 1f, 0.1875f, 1f)
        };
        private static readonly Cuboidf[] BottomCenterBoxes =
        {
            new Cuboidf(0f, 0f, 0f, 1f, 0.1875f, 1f),
            new Cuboidf(0.0625f, 0.1875f, 0.25f, 0.9375f, 1f, 0.75f)
        };
        private static readonly Cuboidf[] MiddleCenterBoxes =
        {
            new Cuboidf(0.1875f, 0f, 0.25f, 0.8125f, 1f, 0.8125f)
        };
        private static readonly Cuboidf[] TopCenterBoxes =
        {
            new Cuboidf(0f, 0f, 0.25f, 1f, 1f, 0.75f)
        };

        public bool TryResolvePlacementPreview(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, out BlockPos targetPos, out Block variant)
        {
            targetPos = null;
            variant = null;
            if (blockSel?.Face == null) return false;

            targetPos = PlacementPreview.DefaultTargetPos(world, blockSel, this);
            string desired = SideFacingPlayer(byPlayer);
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

        private static string SideFacingPlayer(IPlayer byPlayer)
        {
            if (byPlayer?.Entity == null) return null;
            BlockFacing facing = BlockFacing.HorizontalFromYaw(byPlayer.Entity.Pos.Yaw);
            if (facing == BlockFacing.NORTH) return "n";
            if (facing == BlockFacing.EAST)  return "e";
            if (facing == BlockFacing.SOUTH) return "s";
            if (facing == BlockFacing.WEST)  return "w";
            return null;
        }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            if (api.Side != EnumAppSide.Client) return;
            interactions = new[]
            {
                new WorldInteraction { ActionLangCode = "vintagekinematics:blockhelp-counterweight-wind", MouseButton = EnumMouseButton.Right },
                new WorldInteraction { ActionLangCode = "vintagekinematics:blockhelp-counterweight-release", MouseButton = EnumMouseButton.Right, HotKeyCode = "sneak" }
            };
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            return interactions ?? base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
        }

        public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            return CounterweightBoxesForControllerCell();
        }

        public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            return CounterweightBoxesForControllerCell();
        }

        public Cuboidf[] MBGetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos, Vec3i offset)
        {
            return CounterweightBoxesForOffset(offset);
        }

        public Cuboidf[] MBGetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos, Vec3i offset)
        {
            return CounterweightBoxesForOffset(offset);
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (KineticInteractionHelper.ShouldDeferToHeldWrench(byPlayer)) return false;
            if (world.Side == EnumAppSide.Server) Interact(world, byPlayer, blockSel, 0f, first: true);
            return true;
        }

        public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (world.Side == EnumAppSide.Server) Interact(world, byPlayer, blockSel, secondsUsed, first: false);
            return true;
        }

        public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (world.Side == EnumAppSide.Server) EndWinding(world, blockSel);
            base.OnBlockInteractStop(secondsUsed, world, byPlayer, blockSel);
        }

        public override bool OnBlockInteractCancel(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, EnumItemUseCancelReason cancelReason)
        {
            if (world.Side == EnumAppSide.Server) EndWinding(world, blockSel);
            return base.OnBlockInteractCancel(secondsUsed, world, byPlayer, blockSel, cancelReason);
        }

        private void Interact(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, float secondsUsed, bool first)
        {
            if (blockSel == null) return;
            BECounterweightDrive be = MultiblockHelper.GetMultiblockAwareBE(world, blockSel.Position) as BECounterweightDrive;
            if (be == null) return;

            if (byPlayer?.Entity?.Controls?.ShiftKey == true)
            {
                be.Release(ReleaseDirectionFor(be));
                return;
            }

            if (first)
            {
                be.BeginWinding();
                return;
            }

            be.ContinueWinding(secondsUsed);
        }

        private static int ReleaseDirectionFor(BECounterweightDrive be)
        {
            return KineticSourceDirection.ForHorizontalSide(be?.Block, "w");
        }

        private static void EndWinding(IWorldAccessor world, BlockSelection blockSel)
        {
            if (blockSel == null) return;
            BECounterweightDrive be = MultiblockHelper.GetMultiblockAwareBE(world, blockSel.Position) as BECounterweightDrive;
            be?.EndWinding();
        }

        private Cuboidf[] CounterweightBoxesForControllerCell()
        {
            return BoxesForCellOffset(0, 0);
        }

        private Cuboidf[] CounterweightBoxesForOffset(Vec3i offsetToController)
        {
            if (offsetToController == null) return CounterweightBoxesForControllerCell();

            int cellY = -offsetToController.Y;
            int span = UsesXSpan() ? -offsetToController.X : -offsetToController.Z;
            return BoxesForCellOffset(cellY, span);
        }

        private bool UsesXSpan()
        {
            string side = Variant?["side"];
            return side == "n" || side == "s";
        }

        private static Cuboidf[] BoxesForCellOffset(int cellY, int span)
        {
            if (span < -1 || span > 1 || cellY < 0 || cellY > 2) return EmptyBoxes;
            if (cellY == 0) return span == 0 ? BottomCenterBoxes : BottomBaseBoxes;
            if (span != 0) return EmptyBoxes;
            return cellY == 1 ? MiddleCenterBoxes : TopCenterBoxes;
        }
    }
}
