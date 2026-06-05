using Vintagestory.API.Common;

namespace VintageKinematics.Api
{
    public static class KineticSourceDirection
    {
        public static int ForHorizontalSide(Block block, string zeroRotationSide)
        {
            return ForHorizontalSide(block?.Variant?["side"], zeroRotationSide);
        }

        public static int ForHorizontalSide(string side, string zeroRotationSide)
        {
            int steps = RotationStepsFromZero(side, zeroRotationSide);
            return steps == 0 || steps == 3 ? 1 : -1;
        }

        private static int RotationStepsFromZero(string side, string zeroRotationSide)
        {
            int sideIndex = SideIndex(side);
            int zeroIndex = SideIndex(zeroRotationSide);
            return (sideIndex - zeroIndex + 4) % 4;
        }

        private static int SideIndex(string side)
        {
            return side switch
            {
                "n" => 0,
                "e" => 1,
                "s" => 2,
                "w" => 3,
                _ => 0
            };
        }
    }
}
