using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.BlockEntities;

namespace VintageKinematics.Blocks
{
    public class BlockHandCrank : BlockAxisOriented, IKineticActivatable
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
                    ActionLangCode = "vintagekinematics:blockhelp-handcrank-wind",
                    MouseButton = EnumMouseButton.Right
                },
                new WorldInteraction
                {
                    ActionLangCode = "vintagekinematics:blockhelp-handcrank-wind-reverse",
                    MouseButton = EnumMouseButton.Right,
                    HotKeyCode = "sneak"
                }
            };
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            return interactions ?? base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
        }

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemStack, BlockSelection blockSel, ref string failureCode)
        {
            if (blockSel?.Face == null)
            {
                return base.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);
            }

            BlockPos targetPos = PlacementPreview.DefaultTargetPos(world, blockSel, this);
            string desiredAxis = GetPlacementVariantAxis(world, byPlayer, itemStack, blockSel);
            Block variant = Variant["axis"] == desiredAxis
                ? this
                : world.GetBlock(CodeWithVariant("axis", desiredAxis)) ?? this;

            bool placed = variant == this
                ? base.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode)
                : variant.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);

            if (placed && world.Side == EnumAppSide.Server)
            {
                ConfigureManualDirection(world, targetPos, blockSel.Face);
            }

            return placed;
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (blockSel == null) return false;
            if (KineticInteractionHelper.ShouldDeferToHeldWrench(byPlayer)) return false;
            BlockEntity be = world.BlockAccessor.GetBlockEntity(blockSel.Position);
            if (be == null) return false;

            BEBehaviorKineticSource src = be.GetBehavior<BEBehaviorKineticSource>();
            if (src == null) return false;

            if (world.Side == EnumAppSide.Server)
            {
                int direction = DirectionForManualWind(be as BEHandCrank, byPlayer?.Entity?.Controls?.ShiftKey == true);
                src.Wind(seconds: KineticGeneratorAttributes.WindSeconds(this, 0.5f), direction: direction);
            }
            return true;
        }

        private static void ConfigureManualDirection(IWorldAccessor world, BlockPos pos, BlockFacing placementFace)
        {
            BEHandCrank be = world.BlockAccessor.GetBlockEntity(pos) as BEHandCrank;
            be?.SetManualClockwiseDirection(ClockwiseDirectionFromFace(be.Block, placementFace));
        }

        private static int DirectionForManualWind(BEHandCrank crank, bool reverse)
        {
            int direction = crank?.ManualClockwiseDirection ?? 1;
            return reverse ? -direction : direction;
        }

        private static int ClockwiseDirectionFromFace(Block block, BlockFacing placementFace)
        {
            string axis = block?.Variant?["axis"] ?? "y";
            if (placementFace == null || !FaceMatchesAxis(placementFace, axis)) return 1;

            int faceSign = FaceSignOnAxis(placementFace, axis);
            return faceSign > 0 ? -1 : 1;
        }

        private static bool FaceMatchesAxis(BlockFacing face, string axis)
        {
            return axis switch
            {
                "x" => face.Axis == EnumAxis.X,
                "y" => face.Axis == EnumAxis.Y,
                "z" => face.Axis == EnumAxis.Z,
                _ => false
            };
        }

        private static int FaceSignOnAxis(BlockFacing face, string axis)
        {
            return axis switch
            {
                "x" => face.Normali.X,
                "y" => face.Normali.Y,
                "z" => face.Normali.Z,
                _ => 1
            };
        }

        public bool OnKineticActivate(IWorldAccessor world, BlockPos targetPos, BlockFacing activatedFace, BlockPos activatorPos, float signedRPM)
        {
            if (world.Side != EnumAppSide.Server) return false;

            BlockEntity be = world.BlockAccessor.GetBlockEntity(targetPos);
            BEBehaviorKineticSource src = be?.GetBehavior<BEBehaviorKineticSource>();
            if (src == null) return false;

            int direction = signedRPM < 0f ? -1 : 1;
            src.Wind(seconds: KineticGeneratorAttributes.WindSeconds(be.Block ?? this, 0.5f), direction: direction);
            return true;
        }
    }
}
