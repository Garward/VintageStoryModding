using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Blocks
{
    public class BlockBehaviorCanonicalDrop : BlockBehavior
    {
        private Dictionary<string, string> variants = new Dictionary<string, string>();
        private JsonItemStack drop;

        public BlockBehaviorCanonicalDrop(Block block) : base(block)
        {
        }

        public override void Initialize(JsonObject properties)
        {
            base.Initialize(properties);

            if (properties["variants"].Exists)
            {
                variants = properties["variants"].AsObject<Dictionary<string, string>>(new Dictionary<string, string>());
            }

            if (properties["drop"].Exists)
            {
                drop = properties["drop"].AsObject<JsonItemStack>(null, block.Code.Domain);
            }
        }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            drop?.Resolve(api.World, "CanonicalDrop for " + block.Code);
        }

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, ref float dropQuantityMultiplier, ref EnumHandling handled)
        {
            ItemStack stack = CanonicalStack(world, Math.Max(1, (int)Math.Round(dropQuantityMultiplier)));
            if (stack == null)
            {
                handled = EnumHandling.PassThrough;
                return null;
            }

            handled = EnumHandling.PreventDefault;
            return new[] { stack };
        }

        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos, ref EnumHandling handled)
        {
            ItemStack stack = CanonicalStack(world, 1);
            if (stack == null)
            {
                handled = EnumHandling.PassThrough;
                return null;
            }

            handled = EnumHandling.PreventDefault;
            return stack;
        }

        private ItemStack CanonicalStack(IWorldAccessor world, int quantity)
        {
            if (drop?.ResolvedItemstack != null)
            {
                ItemStack stack = drop.ResolvedItemstack.Clone();
                stack.StackSize = Math.Max(1, stack.StackSize * quantity);
                return stack;
            }

            if (world == null || variants == null || variants.Count == 0) return null;

            Block canonical = world.GetBlock(block.CodeWithVariants(variants));
            if (canonical == null) return null;

            return new ItemStack(canonical, quantity);
        }
    }
}
