using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;

namespace HandbookCache
{
    public class HandbookCacheModSystem : ModSystem
    {
        private const string HarmonyId = "garward.betterhandbook.handbookcache";
        private Harmony harmony;

        public override bool ShouldLoad(EnumAppSide side)
        {
            return side == EnumAppSide.Client;
        }

        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            try
            {
                RecipeExplorer.RecipeExplorerMod.ApplyConfig(RecipeExplorer.BetterHandbookConfigStore.Load(api));
                if (RecipeExplorer.BetterHandbookFeatures.RecipeOverlays && api is Vintagestory.API.Client.ICoreClientAPI clientApi)
                {
                    RecipeExplorer.HandbookRecipeOverlays.SetApi(clientApi);
                    RecipeExplorer.HandbookRecipeAssets.Load(clientApi);
                }
                harmony = new Harmony(HarmonyId);
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                RecipeExplorer.BetterHandbookLog.Info(api, "[BetterHandbook] Handbook cache patches applied.");
            }
            catch (Exception ex)
            {
                api.Logger.Error("[BetterHandbook] Failed to apply handbook cache patches: {0}", ex);
            }
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
            base.Dispose();
        }
    }
}
