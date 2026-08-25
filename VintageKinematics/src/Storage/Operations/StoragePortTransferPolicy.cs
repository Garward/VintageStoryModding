using System;

namespace VintageKinematics.Storage.Operations
{
    /// <summary>Small bounds for storage-port cadence; item ownership remains in the controller.</summary>
    public static class StoragePortTransferPolicy
    {
        public const int DefaultOutputIntervalMs = 250;
        public const int MinimumOutputIntervalMs = 50;
        public const int MaximumOutputIntervalMs = 60_000;

        public static int NormalizeOutputIntervalMs(int configured)
        {
            return Math.Clamp(
                configured,
                MinimumOutputIntervalMs,
                MaximumOutputIntervalMs);
        }

        public static float MaximumItemsPerSecond(int intervalMs, int quantityPerTransfer)
        {
            int safeInterval = NormalizeOutputIntervalMs(intervalMs);
            return Math.Max(0, quantityPerTransfer) * 1000f / safeInterval;
        }
    }
}
