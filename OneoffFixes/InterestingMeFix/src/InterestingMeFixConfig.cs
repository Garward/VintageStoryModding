using Vintagestory.API.Common;

namespace InterestingMeFix
{
    public class InterestingMeFixConfig
    {
        public int ConfigVersion { get; set; } = 0;
        public bool RestoreOreOnMuckSpawnFail { get; set; } = true;
        public bool BypassStoneMuck { get; set; } = false;
        public bool RightClickPickup { get; set; } = true;
        public bool RecoverMuckOnDestroy { get; set; } = true;
        public bool AutoHealMissingBE { get; set; } = true;
        public bool SynchronousMuckBreakHandling { get; set; } = true;
        public bool VintageKinematicsBoreMuckCompatibility { get; set; } = true;
        public bool SieveVanillaNuggetParity { get; set; } = true;

        // When BypassStoneMuck is on, IME's `op: remove /dropsByType` patch leaves rock-* blocks
        // with no drops at all (so they drop themselves as placed blocks). This flag puts vanilla
        // chunk drops back on those blocks so mining stone gives stone chunks again.
        public bool RestoreStoneChunkDrops { get; set; } = true;

        // Existing muck created before SieveVanillaNuggetParity is enabled has no embedded ore
        // grade marker. This fallback keeps old/unmarked ore muck from using IME's 50% nugget roll.
        public double SieveFallbackNuggetsPerOreLayer { get; set; } = 1.0;

        public bool RefinedMuckVanillaYieldTiers { get; set; } = true;
        public double RefinedNoProcessingMultiplier { get; set; } = 1.0;
        public double RefinedBasicProcessingMultiplier { get; set; } = 2.0;
        public double RefinedProperFluxMultiplier { get; set; } = 3.0;
        public double RefinedOptimizedMultiplier { get; set; } = 5.0;
        public double RefinedProperFluxMinimumBonus { get; set; } = 0.099;

        // Fallback for old/unmarked muck where the original ore grade cannot be decoded.
        // 12.5 units/layer matches a normal medium ore layer for most vanilla metals.
        public double RefinedFallbackVanillaUnitsPerOreLayer { get; set; } = 12.5;
        public bool RecoverDisplayOnlyStoneMuckWhenStoneBypassIsEnabled { get; set; } = false;
        public bool DisableMuckSloughWhenStoneBypassIsEnabled { get; set; } = false;
        public bool FixEmptyMuckSolidLayerCleanup { get; set; } = true;

        private const string ConfigFile = "interestingmefix.json";

        public static InterestingMeFixConfig Load(ICoreAPI api)
        {
            InterestingMeFixConfig cfg = null;
            try
            {
                cfg = api.LoadModConfig<InterestingMeFixConfig>(ConfigFile);
            }
            catch
            {
                cfg = null;
            }

            if (cfg == null)
            {
                cfg = new InterestingMeFixConfig();
            }
            else if (cfg.ConfigVersion < 2)
            {
                cfg.SieveVanillaNuggetParity = true;
                cfg.ConfigVersion = 2;
            }
            if (cfg.ConfigVersion < 3)
            {
                cfg.RefinedMuckVanillaYieldTiers = true;
                cfg.ConfigVersion = 3;
            }
            if (cfg.ConfigVersion < 4)
            {
                cfg.RecoverDisplayOnlyStoneMuckWhenStoneBypassIsEnabled = false;
                cfg.ConfigVersion = 4;
            }
            if (cfg.ConfigVersion < 5)
            {
                cfg.SynchronousMuckBreakHandling = true;
                cfg.ConfigVersion = 5;
            }
            if (cfg.ConfigVersion < 6)
            {
                cfg.VintageKinematicsBoreMuckCompatibility = true;
                cfg.ConfigVersion = 6;
            }
            if (cfg.ConfigVersion < 7)
            {
                cfg.DisableMuckSloughWhenStoneBypassIsEnabled = true;
                cfg.ConfigVersion = 7;
            }
            if (cfg.ConfigVersion < 8)
            {
                cfg.FixEmptyMuckSolidLayerCleanup = true;
                cfg.DisableMuckSloughWhenStoneBypassIsEnabled = false;
                cfg.ConfigVersion = 8;
            }

            try
            {
                api.StoreModConfig(cfg, ConfigFile);
            }
            catch
            {
            }

            return cfg;
        }
    }
}
