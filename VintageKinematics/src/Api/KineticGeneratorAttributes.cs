using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace VintageKinematics.Api
{
    public static class KineticGeneratorAttributes
    {
        public static JsonObject Attr(Block block) => block?.Attributes?["vkGenerator"];

        public static int TickMs(Block block, int fallback)
        {
            int value = Attr(block)?["tickMs"].AsInt(fallback) ?? fallback;
            return value < 50 ? 50 : value;
        }

        public static float WindSeconds(Block block, float fallback)
        {
            return Attr(block)?["windSeconds"].AsFloat(fallback) ?? fallback;
        }

        public static float SourceRefreshThresholdSeconds(Block block, float fallback)
        {
            return Attr(block)?["sourceRefreshThresholdSeconds"].AsFloat(fallback) ?? fallback;
        }

        public static float WaterLitresPerSecond(Block block, float fallback)
        {
            return Attr(block)?["waterLitresPerSecond"].AsFloat(fallback) ?? fallback;
        }

        public static float MaxChargeSeconds(Block block, float fallback)
        {
            return Attr(block)?["maxChargeSeconds"].AsFloat(fallback) ?? fallback;
        }

        public static float WindSecondsPerSecond(Block block, float fallback)
        {
            return Attr(block)?["windSecondsPerSecond"].AsFloat(fallback) ?? fallback;
        }

        public static float ClickWindSeconds(Block block, float fallback)
        {
            return Attr(block)?["clickWindSeconds"].AsFloat(fallback) ?? fallback;
        }

        public static float ChargeStress(Block block, float fallback)
        {
            return Attr(block)?["chargeStress"].AsFloat(fallback) ?? fallback;
        }

        public static float ChargeEfficiency(Block block, float fallback)
        {
            return Attr(block)?["chargeEfficiency"].AsFloat(fallback) ?? fallback;
        }

        public static float MaxOutputRPM(Block block, float fallback)
        {
            return Attr(block)?["maxOutputRPM"].AsFloat(fallback) ?? fallback;
        }
    }
}
