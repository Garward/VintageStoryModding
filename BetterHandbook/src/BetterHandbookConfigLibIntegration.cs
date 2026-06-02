using System;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace RecipeExplorer
{
    internal static class BetterHandbookConfigLibIntegration
    {
        private const string SettingChangedEvent = "configlib:betterhandbook:setting-changed";
        private static ICoreClientAPI api;
        private static bool initialized;

        public static void TryInitialize(ICoreClientAPI capi)
        {
            if (initialized || capi == null) return;
            initialized = true;
            api = capi;

            if (!capi.ModLoader.IsModEnabled("configlib"))
            {
                capi.Logger.Debug("[BetterHandbook] ConfigLib not detected; using JSON config only.");
                return;
            }

            capi.Event.RegisterEventBusListener(OnSettingChanged, 0.5, SettingChangedEvent);
            BetterHandbookLog.Info(capi, "[BetterHandbook] ConfigLib detected; optional config GUI enabled.");
        }

        public static void Cleanup()
        {
            if (api != null && initialized)
            {
                try
                {
                    api.Event.UnregisterEventBusListener(OnSettingChanged);
                }
                catch
                {
                    // ConfigLib is optional and may unload first.
                }
            }

            api = null;
            initialized = false;
        }

        private static void OnSettingChanged(string eventName, ref EnumHandling handling, IAttribute data)
        {
            if (!(data is ITreeAttribute tree) || api == null)
            {
                return;
            }

            string code = tree.GetString("setting");
            if (string.IsNullOrWhiteSpace(code))
            {
                return;
            }

            ModConfig config = BetterHandbookLog.Config ?? new ModConfig();
            PropertyInfo property = typeof(ModConfig).GetProperty(code, BindingFlags.Instance | BindingFlags.Public);
            if (property == null || property.PropertyType != typeof(bool) || !property.CanWrite)
            {
                return;
            }

            bool value = tree.GetBool("value");
            property.SetValue(config, value);
            RecipeExplorerMod.ApplyConfig(config);
            BetterHandbookConfigStore.Save(api, config);
        }
    }
}
