using System;
using VRPG.Data.Definitions;

namespace VRPG.Inventory.Gear;

/// <summary>Materializes exact gear mechanics through an available visual chassis.</summary>
public sealed class GearPrototypeFactory
{
    /// <summary>Changes only runtime display metadata; exact canonical bytes remain clone-owned.</summary>
    public PlayerItemPrototype Materialize(PlayerItemPrototype prototype)
    {
        if (!GearItemStateCodec.TryDecode(prototype.ExactState, out var gear, out string error))
            throw new InvalidOperationException(error);
        return prototype with
        {
            AssetCode = ResolveChassis(gear!.VisualChassisAssetCode),
            ExactState = (byte[])prototype.ExactState.Clone(),
            EmbeddedReferences = prototype.EmbeddedReferences == null ? null
                : (CanonicalCollectibleReference[])prototype.EmbeddedReferences.Clone()
        };
    }

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

        string assetCode = ResolveChassis(instance.VisualChassisAssetCode);

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

    private string ResolveChassis(string assetCode)
    {
        if (chassisExists(assetCode)) return assetCode;
        if (!string.IsNullOrWhiteSpace(fallbackAssetCode) && chassisExists(fallbackAssetCode)) return fallbackAssetCode;
        throw new InvalidOperationException($"Gear chassis '{assetCode}' and fallback '{fallbackAssetCode}' are unavailable.");
    }
}
