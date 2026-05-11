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
            wrote |= EnsureSieveOverrideStub(cfg, "game:nugget-*");
            wrote |= EnsureSieveOverrideStub(cfg, "game:gem-*");

            try
            {
                if (wrote || !ConfigFileExists(api))
                    api.StoreModConfig(cfg, ConfigFilename);
            }
            catch (System.Exception ex)
            {
                api.Logger.Warning($"[VintageKinematics] Failed to write {ConfigFilename}: {ex.Message}");
            }

            Network.VanillaMPBridge.StableRPM = cfg.VanillaBridgeStableRPM;
            Network.VanillaMPBridge.CapacityPerTorque = cfg.VanillaBridgeCapacityPerTorque;
            // Clamp to (0,1]: 0 would freeze the smoothed value forever; >1 amplifies noise.
            Network.VanillaMPBridge.TorqueSmoothing = System.MathF.Min(1f, System.MathF.Max(0.001f, cfg.VanillaBridgeTorqueSmoothing));

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

        // Seeds a no-op (1.0) entry so server owners see the per-item override schema in the
        // generated config without changing default behavior.
        private static bool EnsureSieveOverrideStub(VintageKinematicsConfig cfg, string code)
        {
            if (cfg.SieveYieldOverrides.ContainsKey(code)) return false;
            cfg.SieveYieldOverrides[code] = 1f;
            return true;
        }

        private static bool ConfigFileExists(ICoreAPI api)
        {
            try { return api.LoadModConfig<VintageKinematicsConfig>(ConfigFilename) != null; }
            catch { return false; }
        }
    }
}
