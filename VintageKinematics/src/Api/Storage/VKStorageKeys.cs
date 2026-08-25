using Vintagestory.API.Common;

namespace VintageKinematics.Api.Storage
{
    /// <summary>
    /// Shared key and comparison helpers for storage implementations.
    /// </summary>
    public static class VKStorageKeys
    {
        public static ItemKey KeyFor(ItemStack stack)
        {
            return ItemKey.FromStack(stack);
        }

        public static bool CanAggregate(IWorldAccessor world, ItemStack a, ItemStack b)
        {
            if (a == null || b == null) return false;
            if (a.Collectible == null || b.Collectible == null) return false;
            // Storage preserves exact stack state. Vanilla ignores temperature and transition
            // state during normal slot merging and then averages them, but indexed storage has
            // no physical slot on which to run that merge machinery. The first storage version
            // rejects transitionable/temperature-bearing stacks and compares every remaining
            // persisted attribute exactly.
            return a.Equals(world, b);
        }

        public static ItemStack ExtractClone(ItemStack exemplar, int quantity)
        {
            if (exemplar == null || quantity <= 0) return null;
            ItemStack clone = exemplar.Clone();
            clone.StackSize = quantity;
            return clone;
        }
    }
}
