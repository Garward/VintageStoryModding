namespace VintageKinematics.Api
{
    /// <summary>
    /// Defines which world liquids may be collected and the volume represented by a source block.
    /// </summary>
    public static class WorldLiquidPumpPolicy
    {
        public const float LavaSourceLitres = 10f;
        public const int FullLiquidLevel = 7;

        public static bool IsVanillaLavaSource(string domain, string liquidCode, int liquidLevel)
        {
            return domain == "game" && liquidCode == "lava" && liquidLevel == FullLiquidLevel;
        }

        public static bool CanCommitLavaSource(bool sinkIsIronTank, float freeLitres)
        {
            return sinkIsIronTank && freeLitres + 0.0001f >= LavaSourceLitres;
        }
    }
}
