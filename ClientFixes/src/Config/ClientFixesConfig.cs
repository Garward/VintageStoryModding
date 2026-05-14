namespace ClientFixes.Config
{
    public sealed class ClientFixesConfig
    {
        public MovableWindowsConfig MovableWindows { get; set; } = new MovableWindowsConfig();
        public DeltaTimeSafetyConfig DeltaTimeSafety { get; set; } = new DeltaTimeSafetyConfig();
        public PositionSafetyConfig PositionSafety { get; set; } = new PositionSafetyConfig();

        public void Sanitize()
        {
            MovableWindows ??= new MovableWindowsConfig();
            DeltaTimeSafety ??= new DeltaTimeSafetyConfig();
            PositionSafety ??= new PositionSafetyConfig();

            DeltaTimeSafety.MaxDeltaTime = Clamp(DeltaTimeSafety.MaxDeltaTime, 0.01f, 1f);
            DeltaTimeSafety.MinimumDeltaTime = Clamp(DeltaTimeSafety.MinimumDeltaTime, 0.0001f, 0.05f);
            DeltaTimeSafety.PauseThreshold = Clamp(DeltaTimeSafety.PauseThreshold, DeltaTimeSafety.MaxDeltaTime, 5f);
            if (DeltaTimeSafety.LogEveryNInterventions < 1)
            {
                DeltaTimeSafety.LogEveryNInterventions = 1;
            }

            PositionSafety.MaxHorizontalCoordinate = Clamp(PositionSafety.MaxHorizontalCoordinate, 1000, 30000000);
            PositionSafety.MinimumY = Clamp(PositionSafety.MinimumY, -100000, 100000);
            PositionSafety.MaximumY = Clamp(PositionSafety.MaximumY, PositionSafety.MinimumY + 1, 1000000);
            PositionSafety.FallbackY = Clamp(PositionSafety.FallbackY, PositionSafety.MinimumY, PositionSafety.MaximumY);
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }

    public sealed class MovableWindowsConfig
    {
        public bool Enabled { get; set; } = true;

        public bool MakeNewWindowsMovable { get; set; } = true;

        public bool PreventFixedModeSelection { get; set; } = true;
    }

    public sealed class DeltaTimeSafetyConfig
    {
        public bool Enabled { get; set; } = true;

        public bool ClampInvalidDeltaTime { get; set; } = true;

        public bool ClampLargeDeltaTime { get; set; } = true;

        public bool UseTinyDeltaWhenGamePaused { get; set; } = true;

        public bool LogInterventions { get; set; } = false;

        public int LogEveryNInterventions { get; set; } = 1000;

        public float MaxDeltaTime { get; set; } = 0.1f;

        public float PauseThreshold { get; set; } = 0.2f;

        public float MinimumDeltaTime { get; set; } = 0.001f;
    }

    public sealed class PositionSafetyConfig
    {
        public bool Enabled { get; set; } = true;

        public bool FixButterflyPositions { get; set; } = true;

        public bool ClampWeatherReaderPositions { get; set; } = true;

        public bool GuardPlayerBlockSoundLookup { get; set; } = true;

        public double MaxHorizontalCoordinate { get; set; } = 1000000;

        public double MinimumY { get; set; } = -1000;

        public double MaximumY { get; set; } = 10000;

        public double FallbackY { get; set; } = 100;
    }
}
