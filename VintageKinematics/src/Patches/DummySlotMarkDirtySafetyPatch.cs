using HarmonyLib;
using Vintagestory.API.Common;

namespace VintageKinematics.Patches
{
    // The vanilla handbook's GuiHandbookMealRecipePage constructs a DummySlot wired to an
    // unspoilableInventory CreativeInventoryTab named "not-used" — the slot has a non-null
    // Inventory reference but is never registered in inventory.Slots[]. If anything calls
    // slot.MarkDirty() on it, InventoryBase.DidModifyItemSlot throws
    // "Supplied slot is not part of this inventory (not-used)!" and CTDs the client.
    //
    // attributerenderinglibrary ships a Harmony patch on BlockMeal.UpdateAndGetTransitionStates
    // that does exactly that whenever a meal block enters the handbook renderer (any bowl whose
    // transition state needs an update — bricklayers' porcelainbowl-* triggers it because they
    // extend BlockMeal). MarkDirty on a DummySlot is conceptually a no-op (nothing to persist or
    // sync), so suppressing it for orphan DummySlots is the minimum safe fix.
    [HarmonyPatch(typeof(ItemSlot), nameof(ItemSlot.MarkDirty))]
    public static class DummySlotMarkDirtySafetyPatch
    {
        static bool Prefix(ItemSlot __instance)
        {
            if (__instance is DummySlot && __instance.Inventory != null && __instance.Inventory.GetSlotId(__instance) < 0)
            {
                return false;
            }
            return true;
        }
    }
}
