using System;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VintageKinematics.Api
{
    public static class BarrelAutomation
    {
        public static int TryPushItemIntoBarrel(IWorldAccessor world, BlockEntityBarrel barrel, ItemSlot source, int maxQuantity = int.MaxValue)
        {
            if (world == null || barrel == null || barrel.Sealed) return 0;
            if (source == null || source.Empty || source.Itemstack?.Collectible == null || maxQuantity <= 0) return 0;
            if (BlockLiquidContainerBase.GetContainableProps(source.Itemstack) != null) return 0;

            IInventory inventory = barrel.Inventory;
            if (inventory == null || inventory.PutLocked || inventory.Count < 2) return 0;

            ItemSlot target = inventory[0];
            if (target == null) return 0;

            int allowed = GetAllowedRecipeInputQuantity(world, barrel, source.Itemstack, Math.Min(maxQuantity, source.Itemstack.StackSize));
            if (allowed <= 0) return 0;

            int moved = source.TryPutInto(world, target, allowed);
            if (moved <= 0) return 0;

            target.MarkDirty();
            barrel.MarkDirty(true);
            return moved;
        }

        private static int GetAllowedRecipeInputQuantity(IWorldAccessor world, BlockEntityBarrel barrel, ItemStack incoming, int requested)
        {
            ICoreAPI api = barrel.Api;
            IInventory inventory = barrel.Inventory;
            if (api == null || inventory == null || inventory.Count < 2) return 0;

            ItemSlot itemSlot = inventory[0];
            ItemSlot liquidSlot = inventory[1];
            if (liquidSlot == null || liquidSlot.Empty) return 0;

            int existingItems = itemSlot?.StackSize ?? 0;
            int bestAllowed = 0;

            foreach (BarrelRecipe recipe in api.GetBarrelRecipes())
            {
                BarrelRecipeIngredient[] ingredients = recipe.Ingredients;
                if (ingredients == null || ingredients.Length != 2) continue;

                for (int i = 0; i < ingredients.Length; i++)
                {
                    BarrelRecipeIngredient itemIngredient = ingredients[i];
                    BarrelRecipeIngredient liquidIngredient = ingredients[1 - i];

                    if (!MatchesIngredient(recipe, incoming, itemIngredient, requireQuantity: false)) continue;
                    if (!MatchesIngredient(recipe, liquidSlot.Itemstack, liquidIngredient, requireQuantity: true)) continue;
                    if (itemSlot != null && !itemSlot.Empty && !MatchesIngredient(recipe, itemSlot.Itemstack, itemIngredient, requireQuantity: false)) continue;

                    int batches = MaxBatchesFromIngredient(liquidSlot.StackSize, liquidIngredient);
                    if (batches <= 0) continue;

                    int targetTotal = itemIngredient.Quantity * batches;
                    if (targetTotal <= existingItems) continue;

                    bestAllowed = Math.Max(bestAllowed, targetTotal - existingItems);
                }
            }

            return Math.Min(requested, bestAllowed);
        }

        private static int MaxBatchesFromIngredient(int stackSize, BarrelRecipeIngredient ingredient)
        {
            if (ingredient == null || ingredient.Quantity <= 0) return 0;
            if (!ingredient.ConsumeQuantity.HasValue && stackSize % ingredient.Quantity != 0) return 0;
            return stackSize / ingredient.Quantity;
        }

        private static bool MatchesIngredient(BarrelRecipe recipe, ItemStack stack, BarrelRecipeIngredient ingredient, bool requireQuantity)
        {
            if (stack?.Collectible == null || ingredient == null) return false;
            if (requireQuantity && stack.StackSize < ingredient.Quantity) return false;

            ItemStack probe = stack;
            if (!requireQuantity && probe.StackSize < ingredient.Quantity)
            {
                probe = stack.Clone();
                probe.StackSize = ingredient.Quantity;
            }

            if (!ingredient.SatisfiesAsIngredient(probe)) return false;
            return probe.Collectible.MatchesForCrafting(probe, recipe, ingredient);
        }
    }
}
