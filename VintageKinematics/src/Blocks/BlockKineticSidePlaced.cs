using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;

namespace VintageKinematics.Blocks
{
    public class BlockKineticSidePlaced : Block, IPlacementPreviewProvider
    {
        public bool TryResolvePlacementPreview(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, out BlockPos targetPos, out Block variant)
        {
            targetPos = null;
            variant = null;
            if (blockSel?.Face == null) return false;

            targetPos = PlacementPreview.DefaultTargetPos(world, blockSel, this);
            string desired = SideForPlacement(byPlayer);
            if (desired == null)
            {
                variant = this;
                return true;
            }

            variant = world.GetBlock(PlacementVariantCode(desired)) ?? this;
            return true;
        }

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemStack, BlockSelection blockSel, ref string failureCode)
        {
            if (!TryResolvePlacementPreview(world, byPlayer, blockSel, out _, out Block variant) || variant == this)
                return base.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);
            return variant.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);
        }

        protected virtual string SideForPlacement(IPlayer byPlayer)
        {
            JsonObject placementAttr = Attributes?["vkPlacement"];
            string convention = placementAttr?["side"].AsString("player") ?? "player";
            return convention == "oppositePlayer"
                ? PlacementPreview.CardinalSideOppositePlayerYaw(byPlayer)
                : PlacementPreview.CardinalSideFromPlayerYaw(byPlayer);
        }

        protected virtual AssetLocation PlacementVariantCode(string side)
        {
            JsonObject placementAttr = Attributes?["vkPlacement"];
            Dictionary<string, string> fixedVariants = placementAttr?["fixedVariants"].AsObject<Dictionary<string, string>>(null);
            if (fixedVariants == null || fixedVariants.Count == 0) return CodeWithVariant("side", side);

            var variants = new Dictionary<string, string>(fixedVariants)
            {
                ["side"] = side
            };
            return CodeWithVariants(variants);
        }
    }
}
