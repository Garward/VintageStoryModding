using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace HerbariumBerryBushFix
{
    public sealed class HerbariumBerryBushFixModSystem : ModSystem
    {
        private const string HarmonyId = "garward.herbariumberrybushfix";
        private Harmony harmony;
        private ICoreAPI api;
        private static ICoreAPI loggerApi;

        public override double ExecuteOrder()
        {
            return 1.1;
        }

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            this.api = api;
            loggerApi = api;
            harmony = new Harmony(HarmonyId);

            PatchTallBerryBush(api);
            PatchPieFoodCategory(api);
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
            api = null;
            loggerApi = null;
            base.Dispose();
        }

        private void PatchTallBerryBush(ICoreAPI api)
        {
            Type targetType = AccessTools.TypeByName("herbarium.BETallBerryBush");
            if (targetType == null)
            {
                api.Logger.Warning("[HerbariumBerryBushFix] herbarium.BETallBerryBush was not found; patch skipped.");
                return;
            }

            MethodInfo initialize = AccessTools.Method(targetType, "Initialize", new[] { typeof(ICoreAPI) });
            MethodInfo finalizer = AccessTools.Method(typeof(HerbariumBerryBushFixModSystem), nameof(FinalizeTallBerryBushInitialize));
            if (initialize == null || finalizer == null)
            {
                api.Logger.Warning("[HerbariumBerryBushFix] Could not resolve patch methods; patch skipped.");
                return;
            }

            try
            {
                harmony.Patch(initialize, finalizer: new HarmonyMethod(finalizer));
                api.Logger.Notification("[HerbariumBerryBushFix] Patched herbarium.BETallBerryBush.Initialize.");
            }
            catch (Exception ex)
            {
                api.Logger.Error("[HerbariumBerryBushFix] Failed to apply tall berry bush patch: {0}", ex);
            }
        }

        private void PatchPieFoodCategory(ICoreAPI api)
        {
            MethodInfo fillingFoodCategory = AccessTools.Method(typeof(BlockPie), nameof(BlockPie.FillingFoodCategory), new[] { typeof(ItemStack) });
            MethodInfo prefix = AccessTools.Method(typeof(HerbariumBerryBushFixModSystem), nameof(PrefixPieFillingFoodCategory));
            MethodInfo finalizer = AccessTools.Method(typeof(HerbariumBerryBushFixModSystem), nameof(FinalizePieFillingFoodCategory));
            if (fillingFoodCategory == null || prefix == null || finalizer == null)
            {
                api.Logger.Warning("[HerbariumBerryBushFix] Could not resolve pie food-category patch methods; patch skipped.");
                return;
            }

            try
            {
                harmony.Patch(fillingFoodCategory, prefix: new HarmonyMethod(prefix), finalizer: new HarmonyMethod(finalizer));
                api.Logger.Notification("[HerbariumBerryBushFix] Patched BlockPie.FillingFoodCategory safe fallback.");
            }
            catch (Exception ex)
            {
                api.Logger.Error("[HerbariumBerryBushFix] Failed to apply pie food-category patch: {0}", ex);
            }
        }

        private static Exception FinalizeTallBerryBushInitialize(object __instance, Exception __exception)
        {
            if (__exception == null) return null;
            if (__exception is not NullReferenceException) return __exception;

            BlockEntity blockEntity = __instance as BlockEntity;
            if (!HasMissingGrowthBlockAttribute(blockEntity)) return __exception;

            ICoreAPI api = blockEntity.Api;
            Block block = blockEntity.Block;
            string code = block?.Code?.ToString() ?? "(unknown block)";
            string pos = blockEntity.Pos?.ToString() ?? "(unknown position)";

            api?.Logger.Warning(
                "[HerbariumBerryBushFix] Suppressed Herbarium tall berry bush init failure at {0} for {1}: missing/empty growthBlock attribute.",
                pos,
                code);

            return null;
        }

        private static bool PrefixPieFillingFoodCategory(ItemStack stack, ref EnumFoodCategory __result)
        {
            try
            {
                FoodNutritionProperties nutritionProps = stack?.Collectible?.NutritionProps
                    ?? stack?.ItemAttributes?["nutritionPropsWhenInMeal"]?.AsObject<FoodNutritionProperties>();

                __result = nutritionProps?.FoodCategory ?? EnumFoodCategory.Vegetable;
                return false;
            }
            catch (NullReferenceException)
            {
                __result = EnumFoodCategory.Vegetable;
                loggerApi?.Logger.Warning(
                    "[HerbariumBerryBushFix] Suppressed pie food-category null while reading filling metadata; using fallback category {0}.",
                    __result);

                return false;
            }
        }

        private static Exception FinalizePieFillingFoodCategory(ItemStack stack, ref EnumFoodCategory __result, Exception __exception)
        {
            if (__exception == null) return null;
            if (__exception is not NullReferenceException) return __exception;

            __result = EnumFoodCategory.Vegetable;
            loggerApi?.Logger.Warning(
                "[HerbariumBerryBushFix] Suppressed pie food-category failure; using fallback category {0}.",
                __result);

            return null;
        }

        private static bool HasMissingGrowthBlockAttribute(BlockEntity blockEntity)
        {
            if (blockEntity?.Block == null) return false;

            JsonObject attributes = blockEntity.Block.Attributes;
            if (attributes == null) return true;

            try
            {
                JsonObject growthBlock = attributes["growthBlock"];
                if (growthBlock == null || !growthBlock.Exists) return true;

                string value = growthBlock.AsString(null);
                return string.IsNullOrWhiteSpace(value);
            }
            catch
            {
                return true;
            }
        }
    }
}
