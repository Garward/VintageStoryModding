using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using VRPG.Data;
using VRPG.Data.Definitions;

namespace VRPG.Inventory.Gear;

public sealed record StarterGearItem(string RoleCode, PlayerItemPrototype Prototype);

public sealed record StarterGearSetResult(bool Success, string ClassCode, StarterGearItem[] Items, string Error)
{
    public static StarterGearSetResult Reject(string classCode, string error) =>
        new(false, classCode, Array.Empty<StarterGearItem>(), error);
    public static StarterGearSetResult Accept(string classCode, StarterGearItem[] items) =>
        new(true, classCode, items, "");
}

/// <summary>Authors deterministic starter prototypes without touching player or inventory state.</summary>
public sealed class StarterGearSetFactory
{
    private readonly VRPGDataRegistry data;
    private readonly GearInstanceAuthor author;
    private readonly GearPrototypeFactory prototypeFactory;

    public StarterGearSetFactory(VRPGDataRegistry data, GearInstanceAuthor author, GearPrototypeFactory prototypeFactory)
    {
        this.data = data ?? throw new ArgumentNullException(nameof(data));
        this.author = author ?? throw new ArgumentNullException(nameof(author));
        this.prototypeFactory = prototypeFactory ?? throw new ArgumentNullException(nameof(prototypeFactory));
    }

    public StarterGearSetResult Create(string classCode)
    {
        ClassDefinition? definition = data.Classes.Get(classCode);
        if (definition?.Starter == null)
            return StarterGearSetResult.Reject(classCode, "Unknown or non-starter class.");
        GearRarityDefinition? common = data.Rarities.Get("vrpg:common");
        if (common == null)
            return StarterGearSetResult.Reject(classCode, "Common rarity is unavailable.");

        var errors = new List<string>();
        // The prototype factory checks chassis availability below; it must not use its recovery fallback for starters.
        StarterLoadoutDefinitionValidator.ValidateItems(definition.Code, definition.Starter, data.GearBases.All, _ => true, errors);
        if (errors.Count > 0) return StarterGearSetResult.Reject(definition.Code, string.Join(" ", errors));

        var items = new List<StarterGearItem>();
        foreach (StarterItemDefinition item in definition.Starter.Items)
        {
            GearBaseDefinition? gearBase = data.GearBases.Get(item.GearBaseCode);
            if (gearBase == null)
                return StarterGearSetResult.Reject(definition.Code, "Starter gear base is unavailable: " + item.GearBaseCode);
            GearAuthoringResult authored = author.Author(new GearAuthoringRequest(
                gearBase.Code, 1, common.Code, Array.Empty<GearAffixSelection>(),
                "starter", definition.Code, new[] { definition.Code }, StableSeed(definition.Code, item.EquipmentRole)));
            if (!authored.Success || authored.Instance == null)
                return StarterGearSetResult.Reject(definition.Code, authored.Error);
            try
            {
                PlayerItemPrototype prototype = prototypeFactory.Create(authored.Instance, gearBase, common);
                if (!string.Equals(prototype.AssetCode, gearBase.VisualChassis, StringComparison.Ordinal))
                    return StarterGearSetResult.Reject(definition.Code, "Starter chassis is unavailable: " + gearBase.VisualChassis);
                items.Add(new StarterGearItem(item.EquipmentRole, prototype));
            }
            catch (InvalidOperationException exception)
            {
                return StarterGearSetResult.Reject(definition.Code, exception.Message);
            }
        }
        return StarterGearSetResult.Accept(definition.Code, items.ToArray());
    }

    private static ulong StableSeed(string classCode, string roleCode) =>
        BinaryPrimitives.ReadUInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(classCode + "\n" + roleCode)));
}
