using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using ClientFixes.Config;

namespace ClientFixes
{
    public sealed class ClientFixesModSystem : ModSystem
    {
        private const string HarmonyId = "garward.clientfixes";
        private const string ConfigFileName = "clientfixes.json";

        private Harmony harmony;

        public static ICoreClientAPI Api { get; private set; }
        public static ClientFixesConfig Config { get; private set; } = new ClientFixesConfig();

        public override bool ShouldLoad(EnumAppSide side)
        {
            return side == EnumAppSide.Client;
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            Api = api;
            LoadConfig(api);

            try
            {
                harmony = new Harmony(HarmonyId);
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                api.Logger.Notification("[ClientFixes] Harmony patches applied.");
            }
            catch (Exception ex)
            {
                api.Logger.Error("[ClientFixes] Failed to apply Harmony patches: {0}", ex);
            }
        }

        private static void LoadConfig(ICoreClientAPI api)
        {
            try
            {
                Config = api.LoadModConfig<ClientFixesConfig>(ConfigFileName) ?? new ClientFixesConfig();
                Config.Sanitize();
                api.StoreModConfig(Config, ConfigFileName);
            }
            catch (Exception ex)
            {
                Config = new ClientFixesConfig();
                api.Logger.Error("[ClientFixes] Failed to load config; using defaults: {0}", ex);
            }
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll(HarmonyId);
            Api = null;
            base.Dispose();
        }
    }
}
