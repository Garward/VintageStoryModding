namespace VintageKinematics.Api
{
    public readonly struct WorldLiquidOutputDefinition
    {
        public readonly string SourceBlockCode;
        public readonly string LiquidCode;

        public WorldLiquidOutputDefinition(string sourceBlockCode, string liquidCode)
        {
            SourceBlockCode = sourceBlockCode;
            LiquidCode = liquidCode;
        }
    }

    /// <summary>
    /// Maps stored liquid portions to the full source block produced by a pump.
    /// </summary>
    public static class WorldLiquidOutputPolicy
    {
        public static bool TryResolve(string domain, string path, out WorldLiquidOutputDefinition definition)
        {
            if (domain == "vintagekinematics" && path == "lavaportion")
            {
                definition = new WorldLiquidOutputDefinition("game:lava-still-7", "lava");
                return true;
            }

            if (domain == "game" && path == "waterportion")
            {
                definition = new WorldLiquidOutputDefinition("game:water-still-7", "water");
                return true;
            }

            if (domain == "game" && path == "saltwaterportion")
            {
                definition = new WorldLiquidOutputDefinition("game:saltwater-still-7", "saltwater");
                return true;
            }

            definition = default;
            return false;
        }

        public static bool IsMatchingFullSource(
            string blockDomain,
            string liquidCode,
            int liquidLevel,
            string expectedLiquidCode)
        {
            return blockDomain == "game"
                && liquidCode == expectedLiquidCode
                && liquidLevel == WorldLiquidPumpPolicy.FullLiquidLevel;
        }
    }
}
