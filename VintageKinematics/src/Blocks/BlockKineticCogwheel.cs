using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Network;
using VintageKinematics.Api;

namespace VintageKinematics.Blocks
{
    public class BlockKineticCogwheel : BlockAxisOriented
    {
        protected virtual EnumKineticRole MyRole => EnumKineticRole.SmallCogwheel;
        public EnumKineticRole Role => MyRole;

        // Override resolves both the placement target (with corner redirect for size-mismatched
        // cog clicks) and the axis variant via CogPlacementResolver. Variant resolution then
        // re-dispatches to the correct cogwheel-{x,y,z} block, whose TryPlaceBlock will see the
        // already-redirected blockSel and not redirect again (the new Position is no longer
        // face-adjacent to the originally-clicked cog).
        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemStack, BlockSelection blockSel, ref string failureCode)
        {
            if (blockSel?.Face == null || blockSel.HitPosition == null)
            {
                return base.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);
            }

            CogPlacement r = CogPlacementResolver.Resolve(world, blockSel, this, MyRole);

            if (!CogPlacementResolver.IsValidCogPlacement(world, r.TargetPos, r.Axis))
            {
                failureCode = "claninvalidposition";
                return false;
            }

            if (r.Redirected)
            {
                blockSel = new BlockSelection
                {
                    Position = r.TargetPos,
                    Face = blockSel.Face,
                    HitPosition = blockSel.HitPosition,
                    SelectionBoxIndex = blockSel.SelectionBoxIndex,
                    DidOffset = true,
                };
            }

            return base.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);
        }

        public override string GetPlacementVariantAxis(IWorldAccessor world, BlockSelection blockSel)
        {
            if (blockSel?.Face == null) return Variant["axis"] ?? "y";
            CogPlacement r = CogPlacementResolver.Resolve(world, blockSel, this, MyRole);
            return CogPlacementResolver.AxisToVariant(r.Axis);
        }

        public override bool TryResolvePlacementPreview(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, out BlockPos targetPos, out Block variant)
        {
            targetPos = null;
            variant = null;
            if (blockSel?.HitPosition == null) return false;

            CogPlacement r = CogPlacementResolver.Resolve(world, blockSel, this, MyRole);
            if (!CogPlacementResolver.IsValidCogPlacement(world, r.TargetPos, r.Axis)) return false;

            targetPos = r.TargetPos;
            variant = world.GetBlock(CodeWithVariant("axis", CogPlacementResolver.AxisToVariant(r.Axis))) ?? this;
            return true;
        }
    }
}
