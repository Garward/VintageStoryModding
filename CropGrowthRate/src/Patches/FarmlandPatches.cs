using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using CropGrowthRate.Behaviors;

namespace CropGrowthRate.Patches
{
    internal static class FarmlandPatchState
    {
        [ThreadStatic]
        public static bool InsideVanillaUpdate;
    }

    [HarmonyPatch(typeof(BlockEntityFarmland), "Initialize", new Type[] { typeof(ICoreAPI) })]
    public static class Farmland_Initialize_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityFarmland __instance, ICoreAPI api)
        {
            if (api.Side != EnumAppSide.Server) return;
            if (__instance.GetBehavior<BlockEntityGrowthTicker>() != null) return;
            var beh = new BlockEntityGrowthTicker(__instance);
            __instance.Behaviors.Add(beh);
            beh.Initialize(api, null);
        }
    }

    [HarmonyPatch]
    public static class Farmland_Update_Patch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(BlockEntityFarmland), "Update", new Type[] { typeof(float) });
        }

        [HarmonyPrefix]
        public static void Prefix() { FarmlandPatchState.InsideVanillaUpdate = true; }

        [HarmonyPostfix]
        public static void Postfix() { FarmlandPatchState.InsideVanillaUpdate = false; }
    }

    // Vanilla calls TryGrowCrop from inside its Update() gate. We drive growth from our
    // own ticker, so block vanilla's call to prevent double-advance. Other callers pass.
    [HarmonyPatch(typeof(BlockEntityFarmland), "TryGrowCrop", new Type[] { typeof(double) })]
    public static class Farmland_TryGrowCrop_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref bool __result)
        {
            if (FarmlandPatchState.InsideVanillaUpdate)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
