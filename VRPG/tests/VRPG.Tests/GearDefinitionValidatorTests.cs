using System;
using VRPG.Data;
using VRPG.Data.Definitions;
using VRPG.Data.Library;
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
    public void RejectsNoncanonicalCommonRareAndUnboundedUniqueProfiles()
    {
        GearRarityDefinition common = Rarity("vrpg:common", 1f);
        common.MinAffixes = 1;
        GearRarityDefinition rare = Rarity("vrpg:rare", 1.15f);
        GearRarityDefinition unique = Rarity("vrpg:unique", 1.2f, true);
        unique.MinAffixes = 7;
        unique.MaxAffixes = 7;

        string[] errors = GearDefinitionValidator.Validate(
            new[] { ValidBase() }, new[] { common, rare, unique }, new[] { ValidAffix() }, ChassisExists);

        Assert.Contains(errors, error => error.Contains("common", StringComparison.OrdinalIgnoreCase)
            && error.Contains("0-1", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("rare", StringComparison.OrdinalIgnoreCase)
            && error.Contains("3-5", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("unique", StringComparison.OrdinalIgnoreCase)
            && error.Contains("affix caps", StringComparison.Ordinal));
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
    public void RejectsWeaponOnlyAffixOnArmorWithMisconfiguredWeaponTags()
    {
        GearAffixDefinition affix = ValidAffix();
        affix.AllowedEquipmentRoles = new[] { PlayerEquipmentRoles.MainWeapon };

        string[] errors = GearDefinitionValidator.Validate(
            new[] { ValidArmorBaseWithWeaponTags() }, Rarities(), new[] { affix }, ChassisExists);

        Assert.Contains(errors, error => error.Contains("compatible gear base", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsArmorOnlyAffixOnWeaponWithMisconfiguredArmorTags()
    {
        GearAffixDefinition affix = ValidAffix();
        affix.AllowedEquipmentRoles = new[] { PlayerEquipmentRoles.Body };

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

    [Fact]
    public void RegistryPublishesDomainlessGearDefinitionsWithCanonicalVrpgIdentities()
    {
        GearBaseDefinition gearBase = ValidBase();
        gearBase.Code = "test-sword";
        gearBase.UniqueCode = "test-sword-unique";
        GearAffixDefinition affix = ValidAffix();
        affix.Code = "test-power";
        affix.MinimumRarityCode = "common";
        GearRarityDefinition[] rarities = Rarities();
        foreach (GearRarityDefinition rarity in rarities)
        {
            rarity.Code = rarity.Code.Substring("vrpg:".Length);
        }

        var registry = new VRPGDataRegistry();
        registry.GearBases.Load(new[] { gearBase });
        registry.Rarities.Load(rarities);
        registry.Affixes.Load(new[] { affix });

        Assert.Empty(GearDefinitionValidator.Validate(
            registry.GearBases.All, registry.Rarities.All, registry.Affixes.All, ChassisExists));
        Assert.Equal("vrpg:test-sword", registry.GearBases.Get("vrpg:test-sword")!.Code);
        Assert.Equal("vrpg:common", registry.Rarities.Get("vrpg:common")!.Code);
        Assert.Equal("vrpg:test-power", registry.Affixes.Get("vrpg:test-power")!.Code);
        Assert.Equal("vrpg:common", registry.Affixes.All[0].MinimumRarityCode);
        Assert.Equal("vrpg:test-sword-unique", registry.GearBases.All[0].UniqueCode);
        Assert.Equal("game:test-sword", registry.GearBases.All[0].VisualChassis);

        LibraryIndex library = LibraryIndex.Build(registry);
        Assert.Contains(library.Entries, entry => entry.Code == "vrpg:test-sword");
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

    private static GearBaseDefinition ValidArmorBaseWithWeaponTags()
    {
        return new GearBaseDefinition
        {
            Code = "vrpg:test-body",
            Name = "Test Body",
            EquipmentRole = PlayerEquipmentRoles.Body,
            Family = "armor",
            Tags = new[] { "weapon" },
            AffixFamilies = new[] { "offense" },
            VisualChassis = "game:test-sword",
            LevelOneArmor = 10f,
            UniqueCode = "vrpg:test-body-unique"
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
            AllowedEquipmentRoles = new[] { PlayerEquipmentRoles.MainWeapon },
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
            Rarity("vrpg:rare", 1.15f, minAffixes: 3, maxAffixes: 5),
            Rarity("vrpg:unique", 1.2f, true)
        };
    }

    private static GearRarityDefinition Rarity(
        string code,
        float weaponPowerScalar,
        bool uniqueLike = false,
        int minAffixes = 0,
        int maxAffixes = 1)
    {
        return new GearRarityDefinition
        {
            Code = code,
            Name = code,
            MinAffixes = minAffixes,
            MaxAffixes = maxAffixes,
            Weight = 1,
            StatScalar = 1f,
            WeaponPowerScalar = weaponPowerScalar,
            UniqueLike = uniqueLike
        };
    }

    private static bool ChassisExists(string code) => code == "game:test-sword";
}
