using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using VintageKinematics.Items;

namespace VintageKinematics.Compatibility
{
    public static class ImmersiveMiningPoweredToolCompat
    {
        private static bool patched;

        public static void Patch(ICoreAPI api, Harmony harmony)
        {
            if (patched || harmony == null) return;

            try
            {
                int patchCount = 0;
                patchCount += PatchMethod(api, harmony,
                    "ImmersiveMining.Patches.Block_OnGettingBroken_Patch",
                    "Prefix",
                    nameof(SkipBlockGettingBrokenPrefix));
                patchCount += PatchMethod(api, harmony,
                    "ImmersiveMining.Patches.ImpactSparkle_AtShakeFrame",
                    "Prefix",
                    nameof(SkipItemslotPatch));
                patchCount += PatchMethod(api, harmony,
                    "ImmersiveMining.Patches.ImpactSparkle_AtShakeFrame",
                    "Postfix",
                    nameof(SkipItemslotPatch));
                patchCount += PatchMethod(api, harmony,
                    "ImmersiveMining.Patches.Collectible_GetHeldTpHitAnimation_Patch",
                    "Postfix",
                    nameof(SkipHeldAnimationPostfix));

                if (patchCount > 0)
                {
                    patched = true;
                    api.Logger.Notification("[VintageKinematics] Applied ImmersiveMining powered tool compatibility.");
                }
            }
            catch (Exception ex)
            {
                api.Logger.Warning("[VintageKinematics] Failed to apply ImmersiveMining powered tool compatibility: {0}", ex);
            }
        }

        private static int PatchMethod(ICoreAPI api, Harmony harmony, string typeName, string methodName, string prefixName)
        {
            Type targetType = AccessTools.TypeByName(typeName);
            if (targetType == null) return 0;

            MethodInfo targetMethod = AccessTools.Method(targetType, methodName);
            MethodInfo prefixMethod = AccessTools.Method(typeof(ImmersiveMiningPoweredToolCompat), prefixName);
            if (targetMethod == null || prefixMethod == null)
            {
                api.Logger.Warning("[VintageKinematics] ImmersiveMining compat target missing: {0}.{1}", typeName, methodName);
                return 0;
            }

            harmony.Patch(targetMethod, prefix: new HarmonyMethod(prefixMethod));
            return 1;
        }

        public static bool SkipBlockGettingBrokenPrefix(ItemSlot itemslot, ref bool __result)
        {
            if (!IsPoweredTool(itemslot)) return true;

            // Let the original block-breaking method run normally, but skip ImmersiveMining's
            // per-impact damage gate. VK powered tools use continuous powered breaking instead.
            __result = true;
            return false;
        }

        public static bool SkipItemslotPatch(ItemSlot itemslot)
        {
            return !IsPoweredTool(itemslot);
        }

        public static bool SkipHeldAnimationPostfix(ItemSlot slot)
        {
            return !IsPoweredTool(slot);
        }

        private static bool IsPoweredTool(ItemSlot slot)
        {
            return slot?.Itemstack?.Collectible is ItemPoweredDrill;
        }
    }
}
