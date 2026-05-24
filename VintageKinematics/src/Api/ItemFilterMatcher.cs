using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Shared whitelist/blacklist matcher for ghost filter inventories.
    /// </summary>
    public static class ItemFilterMatcher
    {
        public static bool Matches(ItemStack stack, IInventory filterInventory, int activeSlots, bool whitelist, bool fuzzy)
        {
            if (stack?.Collectible == null || filterInventory == null) return false;

            int count = System.Math.Min(activeSlots, filterInventory.Count);
            bool anyFilled = false;
            bool anyMatch = false;

            for (int i = 0; i < count; i++)
            {
                ItemSlot filterSlot = filterInventory[i];
                if (filterSlot == null || filterSlot.Empty) continue;

                anyFilled = true;
                bool match = fuzzy
                    ? WildcardUtil.Match(ToFuzzyPattern(filterSlot.Itemstack.Collectible.Code), stack.Collectible.Code)
                    : filterSlot.Itemstack.Collectible == stack.Collectible;

                if (match)
                {
                    anyMatch = true;
                    break;
                }
            }

            if (!anyFilled) return !whitelist;
            return whitelist ? anyMatch : !anyMatch;
        }

        // Fuzzy pattern: keep everything up to the last '-' segment and replace it with '*'.
        // game:nugget-copper -> game:nugget-* ; game:sand-red -> game:sand-*
        public static AssetLocation ToFuzzyPattern(AssetLocation code)
        {
            string path = code.Path;
            int lastDash = path.LastIndexOf('-');
            if (lastDash < 0) return code;
            return new AssetLocation(code.Domain, path.Substring(0, lastDash + 1) + "*");
        }
    }
}
