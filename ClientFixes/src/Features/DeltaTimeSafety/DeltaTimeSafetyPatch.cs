using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Client;
using ClientFixes.Config;

namespace ClientFixes.Features.DeltaTimeSafety
{
    [HarmonyPatch]
    internal static class DeltaTimeSafetyPatch
    {
        private static float lastValidDeltaTime = 0.016f;
        private static int invalidClampCount;
        private static int largeClampCount;

        [HarmonyPatch(typeof(Entity), nameof(Entity.OnGameTick))]
        [HarmonyPrefix]
        private static void EntityOnGameTickPrefix(ref float dt)
        {
            ClampDeltaTime(ref dt, true);
        }

        [HarmonyPatch(typeof(EntityPlayer), "updateEyeHeight")]
        [HarmonyPrefix]
        private static void EntityPlayerUpdateEyeHeightPrefix(ref float dt)
        {
            ClampDeltaTime(ref dt, false);
        }

        private static void ClampDeltaTime(ref float dt, bool log)
        {
            DeltaTimeSafetyConfig config = ClientFixesModSystem.Config?.DeltaTimeSafety;
            if (config == null || !config.Enabled)
            {
                return;
            }

            float original = dt;

            if (config.UseTinyDeltaWhenGamePaused && ClientFixesModSystem.Api is ICoreClientAPI capi && capi.IsGamePaused)
            {
                dt = config.MinimumDeltaTime;
                return;
            }

            if (config.ClampInvalidDeltaTime && (float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0))
            {
                dt = lastValidDeltaTime;
                invalidClampCount++;
                if (ShouldLog(config, log, invalidClampCount))
                {
                    ClientFixesModSystem.Api?.Logger.Warning("[ClientFixes/DeltaTimeSafety] Clamped invalid dt {0} -> {1:F4} count={2}", original, dt, invalidClampCount);
                }
                return;
            }

            if (dt < config.MinimumDeltaTime)
            {
                dt = config.MinimumDeltaTime;
            }
            else if (config.ClampLargeDeltaTime && dt > config.PauseThreshold)
            {
                dt = config.MinimumDeltaTime;
                largeClampCount++;
                if (ShouldLog(config, log, largeClampCount))
                {
                    ClientFixesModSystem.Api?.Logger.Warning("[ClientFixes/DeltaTimeSafety] Used tiny dt after large frame {0:F4}s count={1}", original, largeClampCount);
                }
            }
            else if (config.ClampLargeDeltaTime && dt > config.MaxDeltaTime)
            {
                dt = config.MaxDeltaTime;
                largeClampCount++;
                if (ShouldLog(config, log, largeClampCount))
                {
                    ClientFixesModSystem.Api?.Logger.Warning("[ClientFixes/DeltaTimeSafety] Clamped large dt {0:F4}s -> {1:F4}s count={2}", original, dt, largeClampCount);
                }
            }

            if (!float.IsNaN(dt) && !float.IsInfinity(dt) && dt > 0)
            {
                lastValidDeltaTime = dt;
            }
        }

        private static bool ShouldLog(DeltaTimeSafetyConfig config, bool log, int count)
        {
            return log
                && config.LogInterventions
                && (count == 1 || count % config.LogEveryNInterventions == 0);
        }
    }
}
