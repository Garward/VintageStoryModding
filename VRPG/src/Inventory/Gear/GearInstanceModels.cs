using System;
using System.Collections.Generic;
using System.Linq;

namespace VRPG.Inventory.Gear;

public static class GearInstanceSchema
{
    public const int CurrentVersion = 1;
    public const int CurrentGenerationRules = 2;
}

/// <summary>Modifier operations that may be persisted before definition validation is available.</summary>
public static class GearModifierOperations
{
    public const string Add = "add";
    public const string Increased = "increased";
    public const string More = "more";

    public static bool IsSupported(string? operation) => operation switch
    {
        Add or Increased or More => true,
        _ => false
    };
}

/// <summary>A server-authored permanent gear state with non-aliasing collection accessors.</summary>
public sealed record GearInstance
{
    private GearAffixRoll[] affixes = Array.Empty<GearAffixRoll>();
    private byte[] uniqueRulePayload = Array.Empty<byte>();

    public GearInstance(
        int schemaVersion,
        int generationRuleVersion,
        string baseCode,
        int requiredLevel,
        string rarityCode,
        string equipmentRole,
        string visualChassisAssetCode,
        IEnumerable<GearAffixRoll> affixes,
        GearResolvedStats resolved,
        GearProvenance provenance,
        string uniqueCode,
        byte[] uniqueRulePayload)
    {
        SchemaVersion = schemaVersion;
        GenerationRuleVersion = generationRuleVersion;
        BaseCode = baseCode;
        RequiredLevel = requiredLevel;
        RarityCode = rarityCode;
        EquipmentRole = equipmentRole;
        VisualChassisAssetCode = visualChassisAssetCode;
        Affixes = affixes?.ToArray()!;
        Resolved = resolved;
        Provenance = provenance;
        UniqueCode = uniqueCode;
        UniqueRulePayload = uniqueRulePayload;
    }

    public int SchemaVersion { get; init; }
    public int GenerationRuleVersion { get; init; }
    public string BaseCode { get; init; }
    public int RequiredLevel { get; init; }
    public string RarityCode { get; init; }
    public string EquipmentRole { get; init; }
    public string VisualChassisAssetCode { get; init; }
    public IReadOnlyList<GearAffixRoll> Affixes
    {
        get => Array.AsReadOnly(affixes);
        init => affixes = CloneAffixes(value);
    }

    public GearResolvedStats Resolved { get; init; }
    public GearProvenance Provenance { get; init; }
    public string UniqueCode { get; init; }
    public byte[] UniqueRulePayload
    {
        get => (byte[])uniqueRulePayload.Clone();
        init => uniqueRulePayload = value == null ? Array.Empty<byte>() : (byte[])value.Clone();
    }

    private static GearAffixRoll[] CloneAffixes(IEnumerable<GearAffixRoll>? values) =>
        values?.Select(value => value ?? throw new ArgumentException("Gear affixes cannot contain null values."))
            .ToArray() ?? Array.Empty<GearAffixRoll>();
}

public sealed record GearAffixRoll
{
    private GearModifierRoll[] modifiers = Array.Empty<GearModifierRoll>();

    public GearAffixRoll(string affixCode, int tier, IEnumerable<GearModifierRoll> modifiers)
    {
        AffixCode = affixCode;
        Tier = tier;
        Modifiers = modifiers?.ToArray()!;
    }

    public string AffixCode { get; init; }
    public int Tier { get; init; }
    public IReadOnlyList<GearModifierRoll> Modifiers
    {
        get => Array.AsReadOnly(modifiers);
        init => modifiers = CloneModifiers(value);
    }

    public bool Equals(GearAffixRoll? other) => other != null
        && string.Equals(AffixCode, other.AffixCode, StringComparison.Ordinal)
        && Tier == other.Tier
        && Modifiers.SequenceEqual(other.Modifiers);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(AffixCode, StringComparer.Ordinal);
        hash.Add(Tier);
        foreach (GearModifierRoll modifier in Modifiers) hash.Add(modifier);
        return hash.ToHashCode();
    }

    private static GearModifierRoll[] CloneModifiers(IEnumerable<GearModifierRoll>? values) =>
        values?.Select(value => value ?? throw new ArgumentException("Gear modifiers cannot contain null values."))
            .ToArray() ?? Array.Empty<GearModifierRoll>();
}

public sealed record GearModifierRoll(string Stat, string Operation, float Value);

public sealed record GearResolvedStats(
    float WeaponPower,
    float Armor,
    float MaximumHealth,
    float MaximumMagicShield,
    float Evasion,
    float FireResistance,
    float ColdResistance,
    float LightningResistance,
    float RustResistance);

public sealed record GearProvenance
{
    private string[] tags = Array.Empty<string>();

    public GearProvenance(string sourceKind, string sourceCode, IEnumerable<string> tags, ulong selectionSeed)
    {
        SourceKind = sourceKind;
        SourceCode = sourceCode;
        Tags = tags?.ToArray()!;
        SelectionSeed = selectionSeed;
    }

    public string SourceKind { get; init; }
    public string SourceCode { get; init; }
    public IReadOnlyList<string> Tags
    {
        get => Array.AsReadOnly(tags);
        init => tags = value?.Select(value => value ?? throw new ArgumentException("Gear provenance tags cannot contain null values."))
            .ToArray() ?? Array.Empty<string>();
    }

    public ulong SelectionSeed { get; init; }
}
