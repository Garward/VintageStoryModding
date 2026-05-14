using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using ClientFixes.Config;

namespace ClientFixes.Features.PositionSafety
{
    [HarmonyPatch]
    internal static class ButterflyPositionPatch
    {
        private static int fixedCount;

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method("Vintagestory.GameContent.EntityButterfly:OnGameTick");
        }

        private static void Prefix(object __instance)
        {
            PositionSafetyConfig config = ClientFixesModSystem.Config?.PositionSafety;
            if (config == null || !config.Enabled || !config.FixButterflyPositions)
            {
                return;
            }

            if (__instance is not Entity entity)
            {
                return;
            }

            bool fixedAny = SanitizeEntityPos(entity.Pos, config);
#pragma warning disable CS0618
            fixedAny |= SanitizeEntityPos(entity.ServerPos, config);
            fixedAny |= SanitizeEntityPos(entity.SidedPos, config);
#pragma warning restore CS0618

            if (fixedAny)
            {
                fixedCount++;
                if (fixedCount % 10 == 1)
                {
                    ClientFixesModSystem.Api?.Logger.Warning("[ClientFixes/PositionSafety] Fixed invalid butterfly position count={0}", fixedCount);
                }
            }
        }

        internal static bool SanitizeEntityPos(EntityPos pos, PositionSafetyConfig config)
        {
            if (pos == null)
            {
                return false;
            }

            bool changed = false;

            double x = SanitizeHorizontal(pos.X, config, ref changed);
            double y = SanitizeVertical(pos.Y, config, ref changed);
            double z = SanitizeHorizontal(pos.Z, config, ref changed);

            if (changed)
            {
                pos.SetPos(x, y, z);
            }

            if (pos.Motion != null)
            {
                changed |= SanitizeVec3d(pos.Motion);
            }

            return changed;
        }

        private static double SanitizeHorizontal(double value, PositionSafetyConfig config, ref bool changed)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                changed = true;
                return 0;
            }

            if (value > config.MaxHorizontalCoordinate)
            {
                changed = true;
                return config.MaxHorizontalCoordinate;
            }

            if (value < -config.MaxHorizontalCoordinate)
            {
                changed = true;
                return -config.MaxHorizontalCoordinate;
            }

            return value;
        }

        private static double SanitizeVertical(double value, PositionSafetyConfig config, ref bool changed)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                changed = true;
                return config.FallbackY;
            }

            if (value > config.MaximumY)
            {
                changed = true;
                return config.MaximumY;
            }

            if (value < config.MinimumY)
            {
                changed = true;
                return config.MinimumY;
            }

            return value;
        }

        private static bool SanitizeVec3d(Vec3d vec)
        {
            bool changed = false;

            if (double.IsNaN(vec.X) || double.IsInfinity(vec.X))
            {
                vec.X = 0;
                changed = true;
            }

            if (double.IsNaN(vec.Y) || double.IsInfinity(vec.Y))
            {
                vec.Y = 0;
                changed = true;
            }

            if (double.IsNaN(vec.Z) || double.IsInfinity(vec.Z))
            {
                vec.Z = 0;
                changed = true;
            }

            return changed;
        }
    }

    [HarmonyPatch]
    internal static class WeatherPositionPatch
    {
        private static int fixedCount;

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method("Vintagestory.GameContent.WeatherDataReaderBase:LoadLerp", new[] { typeof(Vec3d), typeof(bool), typeof(float), typeof(float), typeof(float) });
        }

        private static void Prefix(ref Vec3d pos)
        {
            PositionSafetyConfig config = ClientFixesModSystem.Config?.PositionSafety;
            if (config == null || !config.Enabled || !config.ClampWeatherReaderPositions || pos == null)
            {
                return;
            }

            bool changed = false;
            double x = SanitizeHorizontal(pos.X, config, ref changed);
            double y = SanitizeVertical(pos.Y, config, ref changed);
            double z = SanitizeHorizontal(pos.Z, config, ref changed);

            if (changed)
            {
                pos = new Vec3d(x, y, z);
                fixedCount++;
                if (fixedCount % 10 == 1)
                {
                    ClientFixesModSystem.Api?.Logger.Warning("[ClientFixes/PositionSafety] Clamped invalid weather position count={0}", fixedCount);
                }
            }
        }

        private static double SanitizeHorizontal(double value, PositionSafetyConfig config, ref bool changed)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                changed = true;
                return 0;
            }

            if (value > config.MaxHorizontalCoordinate)
            {
                changed = true;
                return config.MaxHorizontalCoordinate;
            }

            if (value < -config.MaxHorizontalCoordinate)
            {
                changed = true;
                return -config.MaxHorizontalCoordinate;
            }

            return value;
        }

        private static double SanitizeVertical(double value, PositionSafetyConfig config, ref bool changed)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                changed = true;
                return config.FallbackY;
            }

            if (value > config.MaximumY)
            {
                changed = true;
                return config.MaximumY;
            }

            if (value < config.MinimumY)
            {
                changed = true;
                return config.MinimumY;
            }

            return value;
        }
    }

    [HarmonyPatch(typeof(EntityPlayer), nameof(EntityPlayer.GetNearestBlockSoundSource))]
    internal static class PlayerSoundPositionPatch
    {
        private static int blockedCount;

        private static bool Prefix(EntityPlayer __instance, ref Block __result)
        {
            PositionSafetyConfig config = ClientFixesModSystem.Config?.PositionSafety;
            if (config == null || !config.Enabled || !config.GuardPlayerBlockSoundLookup)
            {
                return true;
            }

#pragma warning disable CS0618
            EntityPos pos = __instance?.SidedPos;
#pragma warning restore CS0618
            if (pos == null || IsValid(pos.X, pos.Y, pos.Z, config))
            {
                return true;
            }

            __result = null;
            blockedCount++;
            if (blockedCount % 100 == 1)
            {
                ClientFixesModSystem.Api?.Logger.Warning("[ClientFixes/PositionSafety] Blocked player sound lookup with invalid position count={0}", blockedCount);
            }

            return false;
        }

        private static bool IsValid(double x, double y, double z, PositionSafetyConfig config)
        {
            return !double.IsNaN(x) && !double.IsNaN(y) && !double.IsNaN(z)
                && !double.IsInfinity(x) && !double.IsInfinity(y) && !double.IsInfinity(z)
                && Math.Abs(x) <= config.MaxHorizontalCoordinate
                && Math.Abs(z) <= config.MaxHorizontalCoordinate
                && y >= config.MinimumY
                && y <= config.MaximumY;
        }
    }
}
