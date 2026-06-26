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
            return Evaluate(stack, filterInventory, activeSlots, whitelist, fuzzy).Allowed;
        }

        public static ItemFilterResult Evaluate(ItemStack stack, IInventory filterInventory, int activeSlots, bool whitelist, bool fuzzy)
        {
            bool validStack = stack?.Collectible != null;
            if (filterInventory == null) return new ItemFilterResult(false, false, validStack);

            int count = System.Math.Min(activeSlots, filterInventory.Count);
            bool filterExists = false;
            bool anyMatch = false;

            for (int i = 0; i < count; i++)
            {
                ItemSlot filterSlot = filterInventory[i];
                if (filterSlot == null || filterSlot.Empty || filterSlot.Itemstack?.Collectible == null) continue;

                filterExists = true;
                bool match = validStack
                    && (fuzzy
                        ? WildcardUtil.Match(ToFuzzyPattern(filterSlot.Itemstack.Collectible.Code), stack.Collectible.Code)
                        : filterSlot.Itemstack.Collectible.Code.Equals(stack.Collectible.Code));

                if (match)
                {
                    anyMatch = true;
                    break;
                }
            }

            bool allowed = validStack && (whitelist ? filterExists && anyMatch : !filterExists || !anyMatch);
            return new ItemFilterResult(filterExists, anyMatch, allowed);
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

    public readonly struct ItemFilterResult
    {
        public readonly bool FilterExists;
        public readonly bool Matched;
        public readonly bool Allowed;

        public ItemFilterResult(bool filterExists, bool matched, bool allowed)
        {
            FilterExists = filterExists;
            Matched = matched;
            Allowed = allowed;
        }
    }
}
