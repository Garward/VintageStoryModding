using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;

namespace VintageKinematics.Blocks
{
    public class BlockAxisOriented : Block, IPlacementPreviewProvider
    {
        public virtual string GetPlacementVariantAxis(IWorldAccessor world, BlockSelection blockSel)
        {
            if (blockSel?.Face == null) return Variant["axis"] ?? "y";
            return blockSel.Face.Axis switch
            {
                EnumAxis.X => "x",
                EnumAxis.Y => "y",
                EnumAxis.Z => "z",
                _ => "y"
            };
        }

        public virtual string GetPlacementVariantAxis(IWorldAccessor world, IPlayer byPlayer, ItemStack itemStack, BlockSelection blockSel)
        {
            return GetPlacementVariantAxis(world, blockSel);
        }

        public virtual bool TryResolvePlacementPreview(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, out BlockPos targetPos, out Block variant)
        {
            targetPos = null;
            variant = null;
            if (blockSel?.Face == null) return false;

            targetPos = PlacementPreview.DefaultTargetPos(world, blockSel, this);
            string desiredAxis = GetPlacementVariantAxis(world, byPlayer, null, blockSel);
            variant = world.GetBlock(CodeWithVariant("axis", desiredAxis)) ?? this;
            return true;
        }

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemStack, BlockSelection blockSel, ref string failureCode)
        {
            if (blockSel?.Face == null)
            {
                return base.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);
            }

            string desiredAxis = GetPlacementVariantAxis(world, byPlayer, itemStack, blockSel);

            if (Variant["axis"] == desiredAxis)
            {
                return base.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);
            }

            AssetLocation variantCode = CodeWithVariant("axis", desiredAxis);
            Block variant = world.GetBlock(variantCode);
            if (variant == null || variant == this)
            {
                return base.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);
            }
            return variant.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);
        }
    }
}
