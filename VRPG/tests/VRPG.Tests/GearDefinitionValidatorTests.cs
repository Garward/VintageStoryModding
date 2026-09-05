using System;
using VRPG.Data;
using VRPG.Data.Definitions;
using VRPG.Inventory;
using Xunit;

namespace VRPG.Tests;

public sealed class GearDefinitionValidatorTests
{
    [Fact]
    public void RejectsUnknownRoleMissingChassisAndOverlappingAffixTiers()
    {
        var gear = new GearBaseDefinition
        {
            Code = "vrpg:test-body",
            Name = "Test Body",
            EquipmentRole = "armor.elbow",
            Family = "armor",
            Tags = new[] { "armor" },
            AffixFamilies = new[] { "defense" },
            VisualChassis = "missing:coat",
            LevelOneArmor = 35
        };
        var affix = new GearAffixDefinition
        {
            Code = "vrpg:test-defense",
            Name = "Test Defense",
            Family = "defense",
            RequiredTags = new[] { "armor" },
            MinimumRarityCode = "vrpg:common",
            Tiers = new[]
            {
                Tier(1, 1, 20, "vrpg:armor", 1, 2),
                Tier(2, 20, 40, "vrpg:armor", 3, 4)
            }
        };

        string[] errors = GearDefinitionValidator.Validate(
            new[] { gear }, Rarities(), new[] { affix }, _ => false);

        Assert.Contains(errors, value => value.Contains("equipment role", StringComparison.Ordinal));
        Assert.Contains(errors, value => value.Contains("visual chassis", StringComparison.Ordinal));
        Assert.Contains(errors, value => value.Contains("overlap", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsDuplicateNormalizedCodesAndInvalidRarityCapsOrScalars()
    {
        GearBaseDefinition duplicate = ValidBase();
        duplicate.Code = "test-sword";
        GearRarityDefinition invalid = Rarity("vrpg:rare", 1.15f);
        invalid.MinAffixes = 5;
        invalid.MaxAffixes = 4;
        invalid.StatScalar = float.NaN;
        invalid.WeaponPowerScalar = 0f;

        string[] errors = GearDefinitionValidator.Validate(
            new[] { ValidBase(), duplicate },
            new[] { Rarity("common", 1f), invalid, Rarity("vrpg:unique", 1.2f, true) },
            new[] { ValidAffix() },
            ChassisExists);

        Assert.Contains(errors, error => error.Contains("duplicate gear base code", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("affix caps", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("statScalar", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("weaponPowerScalar", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsUnusableAffixFamilyAndMalformedCompatibilityValues()
    {
        GearAffixDefinition affix = ValidAffix();
        affix.Family = "defense";
        affix.RequiredTags = new[] { "weapon", "weapon", "" };
        affix.ExcludedTags = new[] { "weapon", "weapon", "" };
        affix.MutualExclusionGroup = "  ";

        string[] errors = GearDefinitionValidator.Validate(
            new[] { ValidBase() }, Rarities(), new[] { affix }, ChassisExists);

        Assert.Contains(errors, error => error.Contains("family", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("duplicate required tag", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("missing required tag", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("duplicate excluded tag", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("missing excluded tag", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("exclusion group", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAffixTagsThatMatchNoCompatibleBase()
    {
        GearAffixDefinition affix = ValidAffix();
        affix.RequiredTags = new[] { "armor" };

        string[] errors = GearDefinitionValidator.Validate(
            new[] { ValidBase() }, Rarities(), new[] { affix }, ChassisExists);

        Assert.Contains(errors, error => error.Contains("compatible gear base", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsUnsupportedOperationsNonfiniteRollsAndUniqueWithoutAuthoredIdentity()
    {
        GearBaseDefinition baseDefinition = ValidBase();
        baseDefinition.UniqueCode = "";
        GearAffixDefinition affix = ValidAffix();
        affix.Tiers[0].Modifiers = new[]
        {
            new StatModifierDefinition { Stat = "vrpg:weapon_power", Min = float.NegativeInfinity, Max = 2, Operation = "multiply" }
        };

        string[] errors = GearDefinitionValidator.Validate(
            new[] { baseDefinition }, Rarities(), new[] { affix }, ChassisExists);

        Assert.Contains(errors, error => error.Contains("unsupported operation", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("finite", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("authored identity", StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptsCompleteCompatibleDefinitions()
    {
        Assert.Empty(GearDefinitionValidator.Validate(
            new[] { ValidBase() }, Rarities(), new[] { ValidAffix() }, ChassisExists));
    }

    private static GearBaseDefinition ValidBase()
    {
        return new GearBaseDefinition
        {
            Code = "vrpg:test-sword",
            Name = "Test Sword",
            EquipmentRole = PlayerEquipmentRoles.MainWeapon,
            Family = "sword",
            Tags = new[] { "weapon", "sword" },
            AffixFamilies = new[] { "offense" },
            VisualChassis = "game:test-sword",
            LevelOneWeaponPower = 10f,
            UniqueCode = "vrpg:test-sword-unique"
        };
    }

    private static GearAffixDefinition ValidAffix()
    {
        return new GearAffixDefinition
        {
            Code = "vrpg:test-power",
            Name = "Test Power",
            Family = "offense",
            RequiredTags = new[] { "weapon" },
            ExcludedTags = Array.Empty<string>(),
            MutualExclusionGroup = "power",
            UniquePerItem = true,
            MinimumRarityCode = "vrpg:common",
            Tiers = new[] { Tier(1, 1, 100, "vrpg:weapon_power", 1, 4) }
        };
    }

    private static GearAffixTierDefinition Tier(
        int tier,
        int minimumLevel,
        int maximumLevel,
        string stat,
        float min,
        float max)
    {
        return new GearAffixTierDefinition
        {
            Tier = tier,
            MinimumRequiredLevel = minimumLevel,
            MaximumRequiredLevel = maximumLevel,
            Modifiers = new[] { new StatModifierDefinition { Stat = stat, Min = min, Max = max, Operation = "add" } }
        };
    }

    private static GearRarityDefinition[] Rarities()
    {
        return new[]
        {
            Rarity("vrpg:common", 1f),
            Rarity("vrpg:rare", 1.15f),
            Rarity("vrpg:unique", 1.2f, true)
        };
    }

    private static GearRarityDefinition Rarity(string code, float weaponPowerScalar, bool uniqueLike = false)
    {
        return new GearRarityDefinition
        {
            Code = code,
            Name = code,
            MinAffixes = 0,
            MaxAffixes = 1,
            Weight = 1,
            StatScalar = 1f,
            WeaponPowerScalar = weaponPowerScalar,
            UniqueLike = uniqueLike
        };
    }

    private static bool ChassisExists(string code) => code == "game:test-sword";
}
