using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using storagecontroller;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace StorageControllerClientFix
{
    public class StorageControllerClientFixModSystem : ModSystem
    {
        private const string HarmonyId = "storagecontrollerclientfix";
        private Harmony harmony;

        public override bool ShouldLoad(EnumAppSide side)
        {
            return side == EnumAppSide.Client;
        }

        public override double ExecuteOrder()
        {
            return 1.1;
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);

            try
            {
                harmony = new Harmony(HarmonyId);
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                api.Logger.Notification("[StorageControllerClientFix] Harmony patches applied.");
            }
            catch (Exception ex)
            {
                api.Logger.Error("[StorageControllerClientFix] Failed to apply Harmony patches: {0}", ex);
            }
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
            base.Dispose();
        }
    }

    [HarmonyPatch(typeof(GUIDialogStorageAccess), nameof(GUIDialogStorageAccess.FilterItems))]
    internal static class SkipEmptyStorageControllerSearch
    {
        private static readonly FieldInfo CurrentSearchTextField =
            AccessTools.Field(typeof(GUIDialogStorageAccess), "currentSearchText");

        private static readonly FieldInfo CurrentTabField =
            AccessTools.Field(typeof(GUIDialogStorageAccess), "curTab");

        public static bool Prefix(GUIDialogStorageAccess __instance)
        {
            string searchText = CurrentSearchTextField?.GetValue(null) as string;
            if (!string.IsNullOrEmpty(searchText)) return true;

            ResetVisibleSlots(__instance);
            return false;
        }

        private static void ResetVisibleSlots(GUIDialogStorageAccess dialog)
        {
            if (dialog?.StorageVirtualInv == null) return;

            GuiComposer composer = dialog.Composers[dialog.gridCompKey];
            GuiElementItemSlotGrid slotGrid = composer?.GetSlotGrid("slotgrid");
            if (slotGrid == null) return;

            int slotCount = dialog.StorageVirtualInv.Count;
            int curTab = Math.Max(1, CurrentTabField?.GetValue(dialog) as int? ?? 1);
            int start = Math.Max(0, (curTab - 1) * 100);
            int count = Math.Min(100, Math.Max(0, slotCount - start));

            slotGrid.DetermineAvailableSlots(Enumerable.Range(start, count).ToArray());
            composer.Compose(true);
        }
    }
}
