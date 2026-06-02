using System;
using System.Collections.Generic;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace HandbookCache
{
    [HarmonyPatch(typeof(BlockGenericTypedContainer), nameof(BlockGenericTypedContainer.GetDropsForHandbook))]
    internal static class HandbookGenericTypedContainerDropPatch
    {
        private static readonly HashSet<string> ReportedCodes = new HashSet<string>();

        public static bool Prefix(ItemStack handbookStack, IPlayer forPlayer, ref BlockDropItemStack[] __result)
        {
            if (!RecipeExplorer.BetterHandbookFeatures.GenericContainerDropFix) return true;

            ITreeAttribute attributes = handbookStack?.Attributes;
            if (attributes?.GetString("type", null) != null)
            {
                return true;
            }

            string code = handbookStack?.Collectible?.Code?.ToString() ?? "<unknown>";
            lock (ReportedCodes)
            {
                if (ReportedCodes.Add(code))
                {
                    forPlayer?.Entity?.World?.Logger.Notification(
                        "[BetterHandbook] Handbook drop preview skipped for {0}: this generic container entry has no stored type. This is harmless for empty or generated handbook entries.",
                        code);
                }
            }

            __result = Array.Empty<BlockDropItemStack>();
            return false;
        }
    }
}
