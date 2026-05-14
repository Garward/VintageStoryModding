using System.Collections.Generic;
using Vintagestory.API.Common;

namespace CropGrowthRate.Core
{
    public class CropGrowthRateConfig
    {
        public string[] __help = new[]
        {
            "CropGrowthRate config. Server-side only. Restart server to apply.",
            "",
            "growthSpeedMultiplier: base growth speed for ALL crops. Stacks with vanilla's",
            "  moisture+nutrient response curve (depleted soil still drags to ~0.1x).",
            "  Reference (7-stage carrots on freshly-fertile soil, default calendar speed):",
            "    1x   = ~6 IRL hours total",
            "    4.5x = ~80 IRL min total (gentle pace)",
            "    10x  = ~35 IRL min total (Minecraft-ish)",
            "    50x  = ~7 IRL min total",
            "    100x = ~4 IRL min total (superfast / test)",
            "",
            "cropOverrides: per-crop fine-tuning. Keys are AssetLocation patterns with",
            "  wildcards, value is an extra multiplier stacked on growthSpeedMultiplier.",
            "  e.g. 'game:crop-cassava-*': 2.2 -> cassava grows 2.2x faster than the base.",
            "",
            "growthTickIntervalMs: how often each farmland re-checks itself (ms).",
            "growthStagesPerTickMax: cap on stages advanced per tick (prevents runaway",
            "  catch-up if a chunk just loaded after a long offline period).",
            "",
            "syncToWorldConfig: if true, also writes growthSpeedMultiplier to the vanilla",
            "  'cropGrowthRateMul' world.Config key so vanilla client tooltips and the",
            "  farmland info UI display scaled times. Per-crop overrides won't show up",
            "  in those vanilla UIs but the base rate will."
        };

        public float growthSpeedMultiplier = 10f;

        public Dictionary<string, float> cropOverrides = new()
        {
            ["game:crop-cassava-*"] = 2.2f,
            ["game:crop-pineapple-*"] = 2.8f
        };

        public int growthTickIntervalMs = 3000;
        public int growthStagesPerTickMax = 4;

        public bool syncToWorldConfig = true;

        private const string ConfigFile = "CropGrowthRate.json";

        public static CropGrowthRateConfig LoadOrCreate(ICoreAPI api)
        {
            try
            {
                var loaded = api.LoadModConfig<CropGrowthRateConfig>(ConfigFile);
                if (loaded != null) return loaded;
            }
            catch (System.Exception e)
            {
                api.Logger.Error("[CropGrowthRate] Config load failed, using defaults: {0}", e.Message);
            }
            var fresh = new CropGrowthRateConfig();
            api.StoreModConfig(fresh, ConfigFile);
            return fresh;
        }
    }
}
