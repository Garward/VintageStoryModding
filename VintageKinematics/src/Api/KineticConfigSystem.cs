using Vintagestory.API.Common;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Loads and exposes <see cref="VintageKinematicsConfig"/> from
    /// <c>ModConfig/vintagekinematics.json</c>. Seeds defaults for the known blocks
    /// (kineticquern, handcrank) so a fresh install gets a config file the server
    /// owner can edit without first having to know which keys are valid.
    /// </summary>
    public class KineticConfigSystem : ModSystem
    {
        private const string ConfigFilename = "vintagekinematics.json";

        public VintageKinematicsConfig Config { get; private set; }

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            VintageKinematicsConfig cfg = null;
            try { cfg = api.LoadModConfig<VintageKinematicsConfig>(ConfigFilename); }
            catch (System.Exception ex)
            {
                api.Logger.Warning($"[VintageKinematics] Failed to read {ConfigFilename}: {ex.Message} — using defaults.");
            }
            cfg ??= new VintageKinematicsConfig();

            bool wrote = false;
            wrote |= EnsureConsumer(cfg, "kineticquern");
            wrote |= EnsureGenerator(cfg, "handcrank");
            wrote |= EnsureGenerator(cfg, "creativemotor");

            try
            {
                if (wrote || !ConfigFileExists(api))
                    api.StoreModConfig(cfg, ConfigFilename);
            }
            catch (System.Exception ex)
            {
                api.Logger.Warning($"[VintageKinematics] Failed to write {ConfigFilename}: {ex.Message}");
            }

            Config = cfg;
        }

        private static bool EnsureConsumer(VintageKinematicsConfig cfg, string code)
        {
            if (cfg.Consumers.ContainsKey(code)) return false;
            cfg.Consumers[code] = new VintageKinematicsConfig.ConsumerOverride();
            return true;
        }

        private static bool EnsureGenerator(VintageKinematicsConfig cfg, string code)
        {
            if (cfg.Generators.ContainsKey(code)) return false;
            cfg.Generators[code] = new VintageKinematicsConfig.GeneratorOverride();
            return true;
        }

        private static bool ConfigFileExists(ICoreAPI api)
        {
            try { return api.LoadModConfig<VintageKinematicsConfig>(ConfigFilename) != null; }
            catch { return false; }
        }
    }
}
