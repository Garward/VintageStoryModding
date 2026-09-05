using System;
using VRPG.Data.Definitions;

namespace VRPG.Inventory.Gear;

/// <summary>Materializes exact gear mechanics through an available visual chassis.</summary>
public sealed class GearPrototypeFactory
{
    private readonly Func<string, bool> chassisExists;
    private readonly string fallbackAssetCode;

    public GearPrototypeFactory(Func<string, bool> chassisExists, string fallbackAssetCode)
    {
        this.chassisExists = chassisExists ?? throw new ArgumentNullException(nameof(chassisExists));
        this.fallbackAssetCode = fallbackAssetCode ?? throw new ArgumentNullException(nameof(fallbackAssetCode));
    }

    public PlayerItemPrototype Create(
        GearInstance instance,
        GearBaseDefinition gearBase,
        GearRarityDefinition rarity)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(gearBase);
        ArgumentNullException.ThrowIfNull(rarity);

        string assetCode;
        if (chassisExists(instance.VisualChassisAssetCode))
        {
            assetCode = instance.VisualChassisAssetCode;
        }
        else if (chassisExists(fallbackAssetCode))
        {
            assetCode = fallbackAssetCode;
        }
        else
        {
            throw new InvalidOperationException(
                $"Gear chassis '{instance.VisualChassisAssetCode}' and fallback '{fallbackAssetCode}' are unavailable.");
        }

        return new PlayerItemPrototype(
            PlayerItemStorageKind.Unique,
            assetCode,
            PlayerItemCategories.Gear,
            string.Equals(rarity.Code, "vrpg:common", StringComparison.Ordinal)
                ? gearBase.Name
                : $"{rarity.Name} {gearBase.Name}",
            GearItemStateCodec.Encode(instance),
            PlayerItemAssetKind.Item);
    }
}
