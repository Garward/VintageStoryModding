using System;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace SmithingPlusTooltipFix
{
    public sealed class SmithingPlusTooltipFixModSystem : ModSystem
    {
        private const string HarmonyId = "garward.smithingplustooltipfix";
        private Harmony harmony;
        private static ICoreAPI api;
        private static int suppressedCount;

        public override double ExecuteOrder()
        {
            return 1.1;
        }

        public override void StartClientSide(ICoreClientAPI capi)
        {
            base.StartClientSide(capi);
            api = capi;
            harmony = new Harmony(HarmonyId);
            PatchRepairableToolTooltip(capi);
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
            api = null;
            base.Dispose();
        }

        private void PatchRepairableToolTooltip(ICoreAPI api)
        {
            Type targetType = AccessTools.TypeByName("SmithingPlus.ToolRecovery.CollectibleBehaviorRepairableTool");
            if (targetType == null)
            {
                api.Logger.Warning("[SmithingPlusTooltipFix] SmithingPlus repairable-tool behavior was not found; patch skipped.");
                return;
            }

            MethodInfo target = AccessTools.Method(
                targetType,
                "GetHeldItemInfo",
                new[] { typeof(ItemSlot), typeof(StringBuilder), typeof(IWorldAccessor), typeof(bool) });
            MethodInfo finalizer = AccessTools.Method(typeof(SmithingPlusTooltipFixModSystem), nameof(FinalizeGetHeldItemInfo));

            if (target == null || finalizer == null)
            {
                api.Logger.Warning("[SmithingPlusTooltipFix] Could not resolve repairable-tool tooltip patch methods; patch skipped.");
                return;
            }

            try
            {
                harmony.Patch(target, finalizer: new HarmonyMethod(finalizer));
                api.Logger.Notification("[SmithingPlusTooltipFix] Patched SmithingPlus repairable-tool tooltip guard.");
            }
            catch (Exception ex)
            {
                api.Logger.Error("[SmithingPlusTooltipFix] Failed to apply repairable-tool tooltip patch: {0}", ex);
            }
        }

        private static Exception FinalizeGetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo, Exception __exception)
        {
            if (__exception == null) return null;
            if (__exception is not NullReferenceException) return __exception;

            suppressedCount++;
            string code = inSlot?.Itemstack?.Collectible?.Code?.ToString() ?? "(unknown item)";

            if (suppressedCount <= 10 || suppressedCount % 100 == 0)
            {
                api?.Logger.Warning(
                    "[SmithingPlusTooltipFix] Suppressed SmithingPlus repair tooltip null for {0}. Suppressed count: {1}.",
                    code,
                    suppressedCount);
            }

            return null;
        }
    }
}
