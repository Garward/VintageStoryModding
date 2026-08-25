using System;

namespace VintageKinematics.Storage.Recovery
{
    internal static class StorageWarehouseId
    {
        public static string Normalize(string warehouseId)
        {
            if (!Guid.TryParse(warehouseId, out Guid parsed) || parsed == Guid.Empty)
            {
                throw new ArgumentException("Warehouse id must be a non-empty UUID.", nameof(warehouseId));
            }

            return parsed.ToString("D");
        }

        public static bool TryNormalize(string warehouseId, out string normalized)
        {
            if (Guid.TryParse(warehouseId, out Guid parsed) && parsed != Guid.Empty)
            {
                normalized = parsed.ToString("D");
                return true;
            }

            normalized = null;
            return false;
        }
    }
}
