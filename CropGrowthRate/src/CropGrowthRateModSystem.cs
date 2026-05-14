using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using CropGrowthRate.Core;

namespace CropGrowthRate
{
    public class CropGrowthRateModSystem : ModSystem
    {
        private const string HarmonyId = "cropgrowthrate";
        private Harmony harmony;

        public CropGrowthRateConfig Config { get; private set; }

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void Start(ICoreAPI api)
        {
            Config = CropGrowthRateConfig.LoadOrCreate(api);
            api.Logger.Notification(
                "[CropGrowthRate] Loaded. growthSpeedMultiplier={0}, overrides={1}",
                Config.growthSpeedMultiplier, Config.cropOverrides?.Count ?? 0);
            api.RegisterBlockEntityBehaviorClass("CropGrowthTicker", typeof(Behaviors.BlockEntityGrowthTicker));
        }

        public override void StartServerSide(ICoreServerAPI sapi)
        {
            if (Config.syncToWorldConfig)
            {
                sapi.World.Config.SetDouble("cropGrowthRateMul", Config.growthSpeedMultiplier);
                sapi.Logger.Notification(
                    "[CropGrowthRate] Set world.Config.cropGrowthRateMul = {0} (syncs to clients for vanilla tooltips/UI).",
                    Config.growthSpeedMultiplier);
            }

            harmony = new Harmony(HarmonyId);
            harmony.PatchAll();
            int patchedCount = 0;
            foreach (var _ in harmony.GetPatchedMethods()) patchedCount++;
            sapi.Logger.Notification("[CropGrowthRate] Harmony patches applied. Patched methods: {0}", patchedCount);
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll(HarmonyId);
        }
    }
}
