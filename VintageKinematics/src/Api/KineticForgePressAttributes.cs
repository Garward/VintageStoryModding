using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Api
{
    public static class KineticForgePressAttributes
    {
        public static JsonObject Attr(Block block) => block?.Attributes?["vkForgePress"];

        public static float OutputPushIntervalMs(Block block, float fallback) => Float(block, "outputPushIntervalMs", fallback);
        public static int OutputPushBatch(Block block, int fallback) => Int(block, "outputPushBatch", fallback);
        public static float HeatTickMs(Block block, float fallback) => Float(block, "heatTickMs", fallback);
        public static float AmbientTemperature(Block block, float fallback) => Float(block, "ambientTemperature", fallback);
        public static float HeatRatePerSecond(Block block, float fallback) => Float(block, "heatRatePerSecond", fallback);
        public static float CoolRatePerSecond(Block block, float fallback) => Float(block, "coolRatePerSecond", fallback);
        public static float InputHeatTransferMultiplier(Block block, float fallback) => Float(block, "inputHeatTransferMultiplier", fallback);
        public static float InputCoolTransferMultiplier(Block block, float fallback) => Float(block, "inputCoolTransferMultiplier", fallback);
        public static int MaxBellowsAssistCount(Block block, int fallback) => Int(block, "maxBellowsAssistCount", fallback);
        public static float BellowsTemperatureBonusPerUnit(Block block, float fallback) => Float(block, "bellowsTemperatureBonusPerUnit", fallback);
        public static float RefractoryLiningTemperatureBonus(Block block, float fallback) => Float(block, "refractoryLiningTemperatureBonus", fallback);
        public static float RefractoryLiningFuelDurationMultiplier(Block block, float fallback) => Float(block, "refractoryLiningFuelDurationMultiplier", fallback);
        public static float BellowsHeatRateBonusPerUnit(Block block, float fallback) => Float(block, "bellowsHeatRateBonusPerUnit", fallback);
        public static float BellowsStackPenaltyReliefPerUnit(Block block, float fallback) => Float(block, "bellowsStackPenaltyReliefPerUnit", fallback);
        public static float MaxBellowsStackPenaltyRelief(Block block, float fallback) => Float(block, "maxBellowsStackPenaltyRelief", fallback);
        public static int RefractoryLiningBrickCost(Block block, int fallback) => Int(block, "refractoryLiningBrickCost", fallback);

        public static Vec3d[] SmokeStackLocalPositions(Block block, Vec3d[] fallback)
        {
            JsonObject entries = Attr(block)?["smokeStackLocalPositions"];
            if (entries == null || !entries.Exists) return fallback;

            var positions = new List<Vec3d>();
            foreach (JsonObject entry in entries.AsArray())
            {
                if (entry == null || !entry.Exists) continue;
                positions.Add(new Vec3d(
                    entry["x"].AsFloat(0f),
                    entry["y"].AsFloat(0f),
                    entry["z"].AsFloat(0f)));
            }
            return positions.Count > 0 ? positions.ToArray() : fallback;
        }

        private static float Float(Block block, string code, float fallback)
        {
            return Attr(block)?[code].AsFloat(fallback) ?? fallback;
        }

        private static int Int(Block block, string code, int fallback)
        {
            return Attr(block)?[code].AsInt(fallback) ?? fallback;
        }
    }
}
