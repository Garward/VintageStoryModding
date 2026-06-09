using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using ClientFixes.Config;

namespace ClientFixes.Features.PieSafety
{
    [HarmonyPatch]
    internal static class PieFillingFoodCategoryPatch
    {
        private static int suppressedCount;

        private static MethodBase TargetMethod()
        {
            Type blockPieType = AccessTools.TypeByName("Vintagestory.GameContent.BlockPie");
            return blockPieType == null
                ? null
                : AccessTools.Method(blockPieType, "FillingFoodCategory", new[] { typeof(ItemStack) });
        }

        private static bool Prefix(ItemStack stack, ref EnumFoodCategory __result)
        {
            PieSafetyConfig config = ClientFixesModSystem.Config?.PieSafety;
            if (config == null || !config.Enabled || !config.GuardFillingFoodCategoryNulls)
            {
                return true;
            }

            if (stack == null)
            {
                __result = EnumFoodCategory.Vegetable;
                return false;
            }

            if (stack.Collectible == null || stack.ItemAttributes == null)
            {
                __result = GetFallbackCategory(stack);
                LogSuppressed(config, "unresolved filling stack");
                return false;
            }

            return true;
        }

        private static Exception Finalizer(ItemStack stack, ref EnumFoodCategory __result, Exception __exception)
        {
            if (__exception == null)
            {
                return null;
            }

            PieSafetyConfig config = ClientFixesModSystem.Config?.PieSafety;
            if (config == null
                || !config.Enabled
                || !config.GuardFillingFoodCategoryNulls
                || __exception is not NullReferenceException)
            {
                return __exception;
            }

            __result = GetFallbackCategory(stack);
            LogSuppressed(config, __exception.Message);
            return null;
        }

        private static EnumFoodCategory GetFallbackCategory(ItemStack stack)
        {
            try
            {
                FoodNutritionProperties nutritionProps = stack?.Collectible?.NutritionProps
                    ?? stack?.ItemAttributes?["nutritionPropsWhenInMeal"]?.AsObject<FoodNutritionProperties>();

                return nutritionProps?.FoodCategory ?? EnumFoodCategory.Vegetable;
            }
            catch
            {
                return EnumFoodCategory.Vegetable;
            }
        }

        private static void LogSuppressed(PieSafetyConfig config, string reason)
        {
            suppressedCount++;
            if (!config.LogSuppressedCrashes || suppressedCount != 1 && suppressedCount % 10 != 0)
            {
                return;
            }

            ClientFixesModSystem.Api?.Logger.Warning(
                "[ClientFixes/PieSafety] Suppressed pie filling food-category failure; using fallback category count={0}: {1}",
                suppressedCount,
                reason);
        }
    }

    [HarmonyPatch]
    internal static class PieHeldItemNamePatch
    {
        private static int suppressedCount;

        private static MethodBase TargetMethod()
        {
            Type blockPieType = AccessTools.TypeByName("Vintagestory.GameContent.BlockPie");
            return blockPieType == null
                ? null
                : AccessTools.Method(blockPieType, "GetHeldItemName", new[] { typeof(ItemStack) });
        }

        private static Exception Finalizer(ItemStack itemStack, ref string __result, Exception __exception)
        {
            if (__exception == null)
            {
                return null;
            }

            PieSafetyConfig config = ClientFixesModSystem.Config?.PieSafety;
            if (config == null
                || !config.Enabled
                || !config.GuardPieNames
                || __exception is not NullReferenceException)
            {
                return __exception;
            }

            __result = FallbackPieName(itemStack);
            LogSuppressed(config, __exception.Message);
            return null;
        }

        internal static string FallbackPieName(ItemStack itemStack)
        {
            try
            {
                string code = itemStack?.Collectible?.Code?.ToShortString();
                if (!string.IsNullOrWhiteSpace(code))
                {
                    return Lang.GetMatchingIfExists("block-" + code) ?? Lang.Get("pie-empty");
                }
            }
            catch
            {
            }

            return Lang.Get("pie-empty");
        }

        private static void LogSuppressed(PieSafetyConfig config, string reason)
        {
            suppressedCount++;
            if (!config.LogSuppressedCrashes || suppressedCount != 1 && suppressedCount % 10 != 0)
            {
                return;
            }

            ClientFixesModSystem.Api?.Logger.Warning(
                "[ClientFixes/PieSafety] Suppressed pie held-name failure; using fallback name count={0}: {1}",
                suppressedCount,
                reason);
        }
    }

    [HarmonyPatch]
    internal static class PiePlacedBlockNamePatch
    {
        private static int suppressedCount;

        private static MethodBase TargetMethod()
        {
            Type blockPieType = AccessTools.TypeByName("Vintagestory.GameContent.BlockPie");
            return blockPieType == null
                ? null
                : AccessTools.Method(blockPieType, "GetPlacedBlockName", new[] { typeof(IWorldAccessor), typeof(BlockPos) });
        }

        private static Exception Finalizer(object __instance, IWorldAccessor world, BlockPos pos, ref string __result, Exception __exception)
        {
            if (__exception == null)
            {
                return null;
            }

            PieSafetyConfig config = ClientFixesModSystem.Config?.PieSafety;
            if (config == null
                || !config.Enabled
                || !config.GuardPieNames
                || __exception is not NullReferenceException)
            {
                return __exception;
            }

            __result = PieHeldItemNamePatch.FallbackPieName(GetPlacedPieStack(world, pos));
            LogSuppressed(config, __exception.Message);
            return null;
        }

        private static ItemStack GetPlacedPieStack(IWorldAccessor world, BlockPos pos)
        {
            try
            {
                object blockEntity = world?.BlockAccessor?.GetBlockEntity(pos);
                if (blockEntity == null)
                {
                    return null;
                }

                object inventoryObject = AccessTools.Property(blockEntity.GetType(), "Inventory")?.GetValue(blockEntity, null)
                    ?? AccessTools.Field(blockEntity.GetType(), "inventory")?.GetValue(blockEntity);

                return inventoryObject is InventoryBase inventory ? inventory[0]?.Itemstack : null;
            }
            catch
            {
                return null;
            }
        }

        private static void LogSuppressed(PieSafetyConfig config, string reason)
        {
            suppressedCount++;
            if (!config.LogSuppressedCrashes || suppressedCount != 1 && suppressedCount % 10 != 0)
            {
                return;
            }

            ClientFixesModSystem.Api?.Logger.Warning(
                "[ClientFixes/PieSafety] Suppressed placed pie name failure; using fallback name count={0}: {1}",
                suppressedCount,
                reason);
        }
    }
}
