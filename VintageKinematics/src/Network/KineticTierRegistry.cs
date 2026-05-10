using System.Collections.Generic;
using Vintagestory.API.Common;

namespace VintageKinematics.Network
{
    /// <summary>
    /// Loads kinetic tier definitions from <c>assets/&lt;modid&gt;/config/kinetic-tiers.json</c>
    /// files at startup. Mods extend the tier system by dropping their own JSON; merge
    /// is "last loaded wins" with a warning on duplicates.
    /// </summary>
    public class KineticTierRegistry : ModSystem
    {
        public override double ExecuteOrder() => 0.4;

        private readonly Dictionary<string, float> tiers = new Dictionary<string, float>();

        public void Register(string code, float maxRPM)
        {
            tiers[code] = maxRPM;
        }

        public bool TryGetMaxRPM(string code, out float maxRPM)
        {
            return tiers.TryGetValue(code, out maxRPM);
        }

        public override void AssetsFinalize(ICoreAPI api)
        {
            base.AssetsFinalize(api);
            var assets = api.Assets.GetMany<KineticTiersFile>(api.Logger, "config/kinetic-tiers.json");
            foreach (var asset in assets.Values)
            {
                if (asset?.Tiers == null) continue;
                foreach (var t in asset.Tiers)
                {
                    if (t == null || string.IsNullOrEmpty(t.Code)) continue;
                    if (tiers.ContainsKey(t.Code))
                        api.Logger.Warning($"[VintageKinematics] Duplicate tier '{t.Code}' (last load wins)");
                    tiers[t.Code] = t.MaxRPM;
                }
            }
            api.Logger.Notification($"[VintageKinematics] Loaded {tiers.Count} kinetic tier(s)");
        }

        private class KineticTiersFile
        {
            public KineticTierEntry[] Tiers { get; set; }
        }

        private class KineticTierEntry
        {
            public string Code { get; set; }
            public float MaxRPM { get; set; }
        }
    }
}
