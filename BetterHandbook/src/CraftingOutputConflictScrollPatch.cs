using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RecipeExplorer;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Util;
using Vintagestory.Common;

namespace HandbookCache
{
    public class CraftingOutputConflictScrollSystem : ModSystem
    {
        private const string HarmonyId = "garward.betterhandbook.craftingoutputconflicts";
        private Harmony harmony;

        public override bool ShouldLoad(EnumAppSide side)
        {
            return side == EnumAppSide.Server;
        }

        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            RecipeExplorerMod.ApplyConfig(BetterHandbookConfigStore.Load(api));
            if (!BetterHandbookFeatures.CraftingOutputConflictScroll) return;

            harmony = new Harmony(HarmonyId);
            harmony.Patch(
                AccessTools.Method(typeof(InventoryCraftingGrid), nameof(InventoryCraftingGrid.ActivateSlot)),
                prefix: new HarmonyMethod(typeof(CraftingOutputActivateSlotPatch), nameof(CraftingOutputActivateSlotPatch.Prefix)));
            harmony.Patch(
                AccessTools.Method(typeof(InventoryCraftingGrid), "FindMatchingRecipe"),
                postfix: new HarmonyMethod(typeof(CraftingOutputFindMatchingRecipePatch), nameof(CraftingOutputFindMatchingRecipePatch.Postfix)));
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
            base.Dispose();
        }
    }

    internal static class CraftingOutputConflictSelector
    {
        private static readonly ConditionalWeakTable<InventoryCraftingGrid, SelectionState> StateByInventory =
            new ConditionalWeakTable<InventoryCraftingGrid, SelectionState>();

        private static readonly AccessTools.FieldRef<InventoryCraftingGrid, ItemSlot[]> InputSlots =
            AccessTools.FieldRefAccess<InventoryCraftingGrid, ItemSlot[]>("slots");

        private static readonly AccessTools.FieldRef<InventoryCraftingGrid, int> GridSize =
            AccessTools.FieldRefAccess<InventoryCraftingGrid, int>("GridSize");

        private static readonly MethodInfo FoundMatchMethod =
            AccessTools.Method(typeof(InventoryCraftingGrid), "FoundMatch");

        internal static bool HasMultipleMatches(InventoryCraftingGrid inventory)
        {
            return FindMatches(inventory).Count > 1;
        }

        internal static bool TryCycle(InventoryCraftingGrid inventory, int wheelDir)
        {
            List<GridRecipe> matches = FindMatches(inventory);
            if (matches.Count <= 1)
            {
                Reset(inventory);
                return false;
            }

            SelectionState state = StateByInventory.GetOrCreateValue(inventory);
            UpdateSignature(state, inventory, matches.Count);
            int direction = wheelDir >= 0 ? 1 : -1;
            state.SelectedIndex = Mod(state.SelectedIndex + direction, matches.Count);
            ApplyMatch(inventory, matches[state.SelectedIndex]);
            return true;
        }

        internal static void ReapplySelection(InventoryCraftingGrid inventory)
        {
            List<GridRecipe> matches = FindMatches(inventory);
            if (matches.Count <= 1)
            {
                Reset(inventory);
                return;
            }

            SelectionState state = StateByInventory.GetOrCreateValue(inventory);
            UpdateSignature(state, inventory, matches.Count);
            if (state.SelectedIndex > 0)
            {
                ApplyMatch(inventory, matches[state.SelectedIndex]);
            }
        }

        private static void Reset(InventoryCraftingGrid inventory)
        {
            if (inventory == null) return;
            if (StateByInventory.TryGetValue(inventory, out SelectionState state))
            {
                state.SelectedIndex = 0;
                state.Signature = null;
            }
        }

        private static void UpdateSignature(SelectionState state, InventoryCraftingGrid inventory, int matchCount)
        {
            string signature = MakeInputSignature(inventory);
            if (!string.Equals(state.Signature, signature, StringComparison.Ordinal))
            {
                state.Signature = signature;
                state.SelectedIndex = 0;
            }

            state.SelectedIndex = Mod(state.SelectedIndex, matchCount);
        }

        private static List<GridRecipe> FindMatches(InventoryCraftingGrid inventory)
        {
            var matches = new List<GridRecipe>();
            if (inventory?.Api?.World?.FastSearchRecipesByIngredient == null) return matches;

            ItemSlot[] slots = InputSlots(inventory);
            if (slots == null) return matches;

            var candidates = new List<GridRecipe>();
            IPlayer player = inventory.Player;
            int gridSize = GridSize(inventory);

            for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                ItemStack inputStack = slots[slotIndex].Itemstack;
                if (inputStack == null || inputStack.StackSize == 0) continue;

                foreach (KeyValuePair<IRecipeIngredientBase, List<IRecipeBase>> entry in inventory.Api.World.FastSearchRecipesByIngredient)
                {
                    if (!entry.Key.SatisfiesAsIngredient(inputStack, checkStackSize: false)) continue;

                    for (int i = 0; i < entry.Value.Count; i++)
                    {
                        if (entry.Value[i] is GridRecipe { Enabled: not false } gridRecipe)
                        {
                            candidates.AddIfNotPresent(gridRecipe);
                        }
                    }
                }
            }

            AddMatches(matches, candidates, player, inventory.Api.World, slots, gridSize, shapeless: false);
            AddMatches(matches, candidates, player, inventory.Api.World, slots, gridSize, shapeless: true);
            return matches;
        }

        private static void AddMatches(List<GridRecipe> matches, List<GridRecipe> candidates, IPlayer player, IWorldAccessor world, ItemSlot[] slots, int gridSize, bool shapeless)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                GridRecipe recipe = candidates[i];
                if (recipe.Shapeless != shapeless) continue;
                if (recipe.Matches(player, world, slots, gridSize))
                {
                    matches.Add(recipe);
                }
            }
        }

        private static void ApplyMatch(InventoryCraftingGrid inventory, GridRecipe recipe)
        {
            FoundMatchMethod.Invoke(inventory, new object[] { recipe });
        }

        private static string MakeInputSignature(InventoryCraftingGrid inventory)
        {
            ItemSlot[] slots = InputSlots(inventory);
            if (slots == null) return "";

            var parts = new string[slots.Length];
            for (int i = 0; i < slots.Length; i++)
            {
                ItemStack stack = slots[i].Itemstack;
                parts[i] = stack?.Collectible == null
                    ? ""
                    : string.Format("{0}:{1}:{2}", stack.Class, stack.Collectible.Id, stack.StackSize);
            }

            return string.Join("|", parts);
        }

        private static int Mod(int value, int divisor)
        {
            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }

        private sealed class SelectionState
        {
            public string Signature;
            public int SelectedIndex;
        }
    }

    [HarmonyPatch(typeof(InventoryCraftingGrid), nameof(InventoryCraftingGrid.ActivateSlot))]
    internal static class CraftingOutputActivateSlotPatch
    {
        public static bool Prefix(InventoryCraftingGrid __instance, int slotId, ref ItemStackMoveOperation op, ref object __result)
        {
            if (!BetterHandbookFeatures.CraftingOutputConflictScroll) return true;
            if (slotId != __instance.Count - 1 || op.MouseButton != EnumMouseButton.Wheel) return true;

            if (!CraftingOutputConflictSelector.TryCycle(__instance, op.WheelDir))
            {
                return true;
            }

            __result = __instance.Api.Side == EnumAppSide.Client
                ? __instance.InvNetworkUtil.GetActivateSlotPacket(slotId, op)
                : null;
            return false;
        }
    }

    [HarmonyPatch(typeof(InventoryCraftingGrid), "FindMatchingRecipe")]
    internal static class CraftingOutputFindMatchingRecipePatch
    {
        public static void Postfix(InventoryCraftingGrid __instance)
        {
            if (!BetterHandbookFeatures.CraftingOutputConflictScroll) return;
            CraftingOutputConflictSelector.ReapplySelection(__instance);
        }
    }

    [HarmonyPatch(typeof(GuiElementItemSlotGridBase), nameof(GuiElementItemSlotGridBase.OnMouseWheel))]
    internal static class CraftingOutputMouseWheelPatch
    {
        private static readonly AccessTools.FieldRef<GuiElementItemSlotGridBase, IInventory> Inventory =
            AccessTools.FieldRefAccess<GuiElementItemSlotGridBase, IInventory>("inventory");

        private static readonly AccessTools.FieldRef<GuiElementItemSlotGridBase, Action<object>> SendPacketHandler =
            AccessTools.FieldRefAccess<GuiElementItemSlotGridBase, Action<object>>("SendPacketHandler");

        public static bool Prefix(GuiElementItemSlotGridBase __instance, ICoreClientAPI api, MouseWheelEventArgs args)
        {
            if (!BetterHandbookFeatures.CraftingOutputConflictScroll) return true;
            if (args.IsHandled || api == null || !__instance.IsPositionInside(api.Input.MouseX, api.Input.MouseY)) return true;
            if (!(Inventory(__instance) is InventoryCraftingGrid craftingInventory)) return true;

            int outputSlotId = craftingInventory.Count - 1;
            for (int i = 0; i < __instance.SlotBounds.Length && i < __instance.renderedSlots.Count; i++)
            {
                if (__instance.renderedSlots.GetKeyAtIndex(i) != outputSlotId) continue;
                if (!__instance.SlotBounds[i].PointInside(api.Input.MouseX, api.Input.MouseY)) continue;
                if (!CraftingOutputConflictSelector.HasMultipleMatches(craftingInventory)) return true;

                SendWheelPacket(__instance, api, craftingInventory, outputSlotId, args.delta);
                args.SetHandled();
                return false;
            }

            return true;
        }

        private static void SendWheelPacket(GuiElementItemSlotGridBase element, ICoreClientAPI api, InventoryCraftingGrid inventory, int slotId, int delta)
        {
            IInventory mouseInventory = api.World.Player.InventoryManager.GetOwnInventory("mouse");
            var op = new ItemStackMoveOperation(api.World, EnumMouseButton.Wheel, 0, EnumMergePriority.AutoMerge, 1)
            {
                WheelDir = delta > 0 ? 1 : -1,
                ActingPlayer = api.World.Player
            };

            object packet = inventory.ActivateSlot(slotId, mouseInventory[0], ref op);
            SendPacket(element, packet);
        }

        private static void SendPacket(GuiElementItemSlotGridBase element, object packet)
        {
            if (packet == null) return;

            Action<object> handler = SendPacketHandler(element);
            if (packet is object[] packets)
            {
                for (int i = 0; i < packets.Length; i++)
                {
                    handler?.Invoke(packets[i]);
                }
                return;
            }

            handler?.Invoke(packet);
        }
    }
}
