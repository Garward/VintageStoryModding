using Vintagestory.API.Common;

namespace InterestingMeFix
{
    public class InterestingMeFixConfig
    {
        public bool RestoreOreOnMuckSpawnFail { get; set; } = true;
        public bool BypassStoneMuck { get; set; } = false;
        public bool RightClickPickup { get; set; } = true;
        public bool RecoverMuckOnDestroy { get; set; } = true;

        // When BypassStoneMuck is on, IME's `op: remove /dropsByType` patch leaves rock-* blocks
        // with no drops at all (so they drop themselves as placed blocks). This flag puts vanilla
        // chunk drops back on those blocks so mining stone gives stone chunks again.
        public bool RestoreStoneChunkDrops { get; set; } = true;

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
