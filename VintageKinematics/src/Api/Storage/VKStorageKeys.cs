using Vintagestory.API.Common;
using Vintagestory.API.Config;

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
            return a.Equals(world, b, GlobalConstants.IgnoredStackAttributes);
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
