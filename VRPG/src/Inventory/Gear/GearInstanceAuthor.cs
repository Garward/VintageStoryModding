using System;
using System.Collections.Generic;
using System.Linq;
using VRPG.Data;
using VRPG.Data.Definitions;

namespace VRPG.Inventory.Gear;

public sealed record GearAuthoringRequest(
    string BaseCode,
    int RequiredLevel,
    string RarityCode,
    GearAffixSelection[] Affixes,
    string SourceKind,
    string SourceCode,
    string[] SourceTags,
    ulong SelectionSeed);

public sealed record GearAffixSelection(string AffixCode, int Tier, float Roll01);

public sealed record GearAuthoringResult(bool Success, GearInstance? Instance, string Error)
{
    public static GearAuthoringResult Reject(string error) => new(false, null, error);
    public static GearAuthoringResult Accept(GearInstance instance) => new(true, instance, "");
}

/// <summary>Validates exact selections and permanently resolves one canonical gear instance.</summary>
public sealed class GearInstanceAuthor
{
    private const int MinimumRequiredLevel = 1;
    private const int MaximumRequiredLevel = 100;

    private readonly VRPGDataRegistry data;

    public GearInstanceAuthor(VRPGDataRegistry data)
    {
        this.data = data ?? throw new ArgumentNullException(nameof(data));
    }

    public GearAuthoringResult Author(GearAuthoringRequest request)
    {
        if (request == null) return GearAuthoringResult.Reject("Gear authoring request is missing.");
        if (request.RequiredLevel is < MinimumRequiredLevel or > MaximumRequiredLevel)
        {
            return GearAuthoringResult.Reject(
                $"Gear required level must be between {MinimumRequiredLevel} and {MaximumRequiredLevel}.");
        }

        GearBaseDefinition? gearBase = data.GearBases.Get(request.BaseCode);
        if (gearBase == null)
        {
            return GearAuthoringResult.Reject($"Unknown gear base '{Display(request.BaseCode)}'.");
        }

        GearRarityDefinition? rarity = data.Rarities.Get(request.RarityCode);
        if (rarity == null)
        {
            return GearAuthoringResult.Reject($"Unknown gear rarity '{Display(request.RarityCode)}'.");
        }

        if (rarity.UniqueLike && string.IsNullOrWhiteSpace(gearBase.UniqueCode))
        {
            return GearAuthoringResult.Reject(
                $"Unique rarity '{rarity.Code}' requires a matching unique identity on gear base '{gearBase.Code}'.");
        }

        string? provenanceError = ValidateProvenance(request);
        if (provenanceError != null) return GearAuthoringResult.Reject(provenanceError);

        foreach (StatModifierDefinition implicitModifier in gearBase.Implicits ?? Array.Empty<StatModifierDefinition>())
        {
            if (implicitModifier == null
                || !float.IsFinite(implicitModifier.Min)
                || !float.IsFinite(implicitModifier.Max)
                || implicitModifier.Min != implicitModifier.Max)
            {
                return GearAuthoringResult.Reject(
                    $"Gear base '{gearBase.Code}' implicits must use fixed finite Min == Max values.");
            }
            if (!GearModifierContract.IsBaked(implicitModifier.Stat)
                || !GearModifierContract.IsSupported(implicitModifier.Stat, implicitModifier.Operation))
            {
                return GearAuthoringResult.Reject(
                    $"Gear base '{gearBase.Code}' implicits must target a supported resolved base stat.");
            }
        }

        GearAffixSelection[] selections = request.Affixes ?? Array.Empty<GearAffixSelection>();
        var selectedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exclusionGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rolls = new List<GearAffixRoll>(selections.Length);

        foreach (GearAffixSelection selection in selections)
        {
            if (selection == null) return GearAuthoringResult.Reject("Gear affix selection cannot be null.");
            GearAffixDefinition? affix = data.Affixes.Get(selection.AffixCode);
            if (affix == null)
            {
                return GearAuthoringResult.Reject($"Unknown gear affix '{Display(selection.AffixCode)}'.");
            }

            GearRarityDefinition? minimumRarity = data.Rarities.Get(affix.MinimumRarityCode);
            if (minimumRarity == null)
            {
                return GearAuthoringResult.Reject(
                    $"Gear affix '{affix.Code}' references unknown minimum rarity '{Display(affix.MinimumRarityCode)}'.");
            }
            if (rarity.ProgressionRank < minimumRarity.ProgressionRank)
            {
                return GearAuthoringResult.Reject(
                    $"Gear affix '{affix.Code}' requires minimum rarity '{minimumRarity.Code}'.");
            }

            if (!selectedCodes.Add(affix.Code))
            {
                return GearAuthoringResult.Reject($"Duplicate gear affix '{affix.Code}'.");
            }

            if (!string.IsNullOrWhiteSpace(affix.MutualExclusionGroup)
                && !exclusionGroups.Add(affix.MutualExclusionGroup))
            {
                return GearAuthoringResult.Reject(
                    $"Gear affix '{affix.Code}' conflicts with another selection in mutual exclusion group '{affix.MutualExclusionGroup}'.");
            }

            string? compatibilityError = CompatibilityError(gearBase, affix);
            if (compatibilityError != null) return GearAuthoringResult.Reject(compatibilityError);

            if (!float.IsFinite(selection.Roll01) || selection.Roll01 is < 0f or > 1f)
            {
                return GearAuthoringResult.Reject(
                    $"Gear affix '{affix.Code}' Roll01 must be finite and between 0 and 1.");
            }

            GearAffixTierDefinition? tier = (affix.Tiers ?? Array.Empty<GearAffixTierDefinition>())
                .FirstOrDefault(candidate => candidate != null && candidate.Tier == selection.Tier);
            if (tier == null)
            {
                return GearAuthoringResult.Reject(
                    $"Gear affix '{affix.Code}' has no tier {selection.Tier}.");
            }
            if (request.RequiredLevel < tier.MinimumRequiredLevel || request.RequiredLevel > tier.MaximumRequiredLevel)
            {
                return GearAuthoringResult.Reject(
                    $"Gear affix '{affix.Code}' tier {selection.Tier} is unavailable at required level {request.RequiredLevel}.");
            }

            try
            {
                GearModifierRoll[] modifiers = (tier.Modifiers ?? Array.Empty<StatModifierDefinition>())
                    .Select(modifier => Roll(modifier, selection.Roll01))
                    .ToArray();
                rolls.Add(new GearAffixRoll(affix.Code, tier.Tier, modifiers));
            }
            catch (ArgumentException exception)
            {
                return GearAuthoringResult.Reject($"Gear affix '{affix.Code}' is invalid: " + exception.Message);
            }
        }

        if (rolls.Count < rarity.MinAffixes || rolls.Count > rarity.MaxAffixes)
        {
            return GearAuthoringResult.Reject(
                $"Gear affix count {rolls.Count} is outside rarity '{rarity.Code}' budget {rarity.MinAffixes}-{rarity.MaxAffixes}.");
        }

        var values = new Dictionary<string, StatLayers>(StringComparer.Ordinal);
        foreach (StatModifierDefinition implicitModifier in gearBase.Implicits ?? Array.Empty<StatModifierDefinition>())
        {
            Add(values, new GearModifierRoll(implicitModifier.Stat, implicitModifier.Operation, implicitModifier.Min));
        }
        foreach (GearAffixRoll affix in rolls)
        {
            foreach (GearModifierRoll modifier in affix.Modifiers)
                if (GearModifierContract.IsBaked(modifier.Stat)) Add(values, modifier);
        }

        GearResolvedStats resolved;
        try
        {
            resolved = ResolveStats(request.RequiredLevel, gearBase, rarity, values);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return GearAuthoringResult.Reject("Gear resolved values are invalid: " + exception.Message);
        }

        var instance = new GearInstance(
            GearInstanceSchema.CurrentVersion,
            GearInstanceSchema.CurrentGenerationRules,
            gearBase.Code,
            request.RequiredLevel,
            rarity.Code,
            gearBase.EquipmentRole,
            gearBase.VisualChassis,
            rolls,
            resolved,
            new GearProvenance(request.SourceKind, request.SourceCode, request.SourceTags, request.SelectionSeed),
            rarity.UniqueLike ? gearBase.UniqueCode : "",
            Array.Empty<byte>());
        try
        {
            // Validate the same immutable wire boundary used by prototypes, including mutated definition identities.
            GearItemStateCodec.Encode(instance);
        }
        catch (ArgumentException exception)
        {
            return GearAuthoringResult.Reject("Gear instance is invalid: " + exception.Message);
        }
        return GearAuthoringResult.Accept(instance);
    }

    private static GearModifierRoll Roll(StatModifierDefinition modifier, float roll01)
    {
        if (modifier == null || !GearModifierContract.IsSupported(modifier.Stat, modifier.Operation))
            throw new ArgumentException("Unsupported gear modifier stat/operation pair.");
        if (!float.IsFinite(modifier.Min) || !float.IsFinite(modifier.Max) || modifier.Max < modifier.Min)
            throw new ArgumentException("Gear modifier endpoints must be finite and ordered.");
        double value = modifier.Min + ((double)modifier.Max - modifier.Min) * roll01;
        if (!double.IsFinite(value) || value < -float.MaxValue || value > float.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(modifier), "Gear modifier value must be finite and float-representable.");
        return new GearModifierRoll(modifier.Stat, modifier.Operation, (float)value);
    }

    private static GearResolvedStats ResolveStats(
        int requiredLevel,
        GearBaseDefinition gearBase,
        GearRarityDefinition rarity,
        IReadOnlyDictionary<string, StatLayers> values)
    {
        StatLayers weapon = Layers(values, "vrpg:weapon_power");
        double weaponPower = GearScalingMath.ResolveWeaponPower(
            GearScalingMath.WeaponLevelBase(requiredLevel, gearBase.LevelOneWeaponPower),
            weapon.Flat,
            weapon.Increased,
            weapon.More,
            rarity.WeaponPowerScalar);

        return new GearResolvedStats(
            ToFloat(weaponPower),
            ResolveOrdinary(values, "vrpg:armor", GearScalingMath.ArmorLevelBase(requiredLevel, gearBase.LevelOneArmor), rarity.StatScalar),
            ResolveOrdinary(values, "vrpg:max_health", 0, rarity.StatScalar),
            ResolveOrdinary(values, "vrpg:max_magic_shield", 0, rarity.StatScalar),
            ResolveOrdinary(values, "vrpg:evasion", 0, rarity.StatScalar),
            ResolveOrdinary(values, "vrpg:fire_resistance", 0, rarity.StatScalar),
            ResolveOrdinary(values, "vrpg:cold_resistance", 0, rarity.StatScalar),
            ResolveOrdinary(values, "vrpg:lightning_resistance", 0, rarity.StatScalar),
            ResolveOrdinary(values, "vrpg:rust_resistance", 0, rarity.StatScalar));
    }

    private static float ResolveOrdinary(
        IReadOnlyDictionary<string, StatLayers> values,
        string stat,
        double baseValue,
        double rarityScalar)
    {
        StatLayers layers = Layers(values, stat);
        double result = Math.Max(0, baseValue + layers.Flat)
            * Math.Max(0, 1 + layers.Increased / 100.0)
            * layers.More.Aggregate(1.0, (product, percent) =>
                product * Math.Max(0, 1 + percent / 100.0))
            * rarityScalar;
        return ToFloat(result);
    }

    private static float ToFloat(double value)
    {
        float result = (float)value;
        if (!double.IsFinite(value) || value > float.MaxValue || value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Resolved gear values must be finite and non-negative.");
        }
        return result;
    }

    private static void Add(IDictionary<string, StatLayers> values, GearModifierRoll modifier)
    {
        if (!values.TryGetValue(modifier.Stat, out StatLayers? layers))
        {
            layers = new StatLayers();
            values.Add(modifier.Stat, layers);
        }

        switch (modifier.Operation)
        {
            case GearModifierOperations.Add:
                layers.Flat += modifier.Value;
                break;
            case GearModifierOperations.Increased:
                layers.Increased += modifier.Value;
                break;
            case GearModifierOperations.More:
                layers.More.Add(modifier.Value);
                break;
        }
    }

    private static StatLayers Layers(IReadOnlyDictionary<string, StatLayers> values, string stat) =>
        values.TryGetValue(stat, out StatLayers? layers) ? layers : new StatLayers();

    private static string? CompatibilityError(GearBaseDefinition gearBase, GearAffixDefinition affix)
    {
        if (!(gearBase.AffixFamilies ?? Array.Empty<string>()).Any(value =>
            string.Equals(value, affix.Family, StringComparison.OrdinalIgnoreCase)))
        {
            return $"Gear affix '{affix.Code}' family is incompatible with gear base '{gearBase.Code}'.";
        }

        var tags = new HashSet<string>(gearBase.Tags ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        if (!(affix.RequiredTags ?? Array.Empty<string>()).All(tags.Contains)
            || (affix.ExcludedTags ?? Array.Empty<string>()).Any(tags.Contains))
        {
            return $"Gear affix '{affix.Code}' tag requirements are incompatible with gear base '{gearBase.Code}'.";
        }

        string[] allowed = affix.AllowedEquipmentRoles ?? Array.Empty<string>();
        string[] excluded = affix.ExcludedEquipmentRoles ?? Array.Empty<string>();
        if ((allowed.Length > 0 && !allowed.Any(role =>
                string.Equals(role, gearBase.EquipmentRole, StringComparison.OrdinalIgnoreCase)))
            || excluded.Any(role => string.Equals(role, gearBase.EquipmentRole, StringComparison.OrdinalIgnoreCase)))
        {
            return $"Gear affix '{affix.Code}' role is incompatible with gear base '{gearBase.Code}'.";
        }

        return null;
    }

    private static string? ValidateProvenance(GearAuthoringRequest request)
    {
        if (!IsNormalized(request.SourceKind)) return "Gear provenance source kind must be nonempty and normalized.";
        if (!IsNormalized(request.SourceCode)) return "Gear provenance source code must be nonempty and normalized.";
        if (request.SourceTags == null) return "Gear provenance tags are missing.";
        foreach (string tag in request.SourceTags)
        {
            if (!IsNormalized(tag)) return "Gear provenance tags must be nonempty and normalized.";
        }
        return null;
    }

    private static bool IsNormalized(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && string.Equals(value, value.Trim().ToLowerInvariant(), StringComparison.Ordinal);

    private static string Display(string? value) => string.IsNullOrWhiteSpace(value) ? "<missing>" : value;

    private sealed class StatLayers
    {
        public double Flat { get; set; }
        public double Increased { get; set; }
        public List<double> More { get; } = new();
    }
}
