using System.Globalization;
using VRPG.Inventory;
using VRPG.Network;

namespace VRPG.Client.PlayerMenu.Inventory;

public static class InventoryItemRow
{
    public static (double Value, string Unit) DisplayValue(double value, string unit) => unit == "fraction" ? (value * 100, "percent") : (value, unit);
    public static bool CanSelectQuantity(PlayerItemEntryPacket row) => row.Location == PlayerItemLocation.Stored
        && row.ReservedQuantity < row.Quantity && !row.Flags.HasFlag(PlayerItemFlags.Protected)
        && row.Kind != PlayerItemStorageKind.Protected;
    public static string ExactQuantity(long quantity) => quantity.ToString(CultureInfo.InvariantCulture);
    public static string QuantityOverlay(long quantity)
    {
        // Decimal avoids loss above the double integer precision boundary; overlays are never parsed back.
        foreach (var (divisor, suffix) in new[] { (1000000000000000000m, "E"), (1000000000000000m, "P"),
            (1000000000000m, "T"), (1000000000m, "B"), (1000000m, "M"), (1000m, "K") })
            if (quantity >= divisor) return (decimal.Truncate(quantity / divisor * 10) / 10).ToString("0.#", CultureInfo.InvariantCulture) + suffix;
        return ExactQuantity(quantity);
    }
    public static string PresentationKey(PlayerItemEntryPacket row) => row.Kind switch
    { PlayerItemStorageKind.Ledger => "balance", PlayerItemStorageKind.Protected => "protected-record", _ => "quantity" };
}
