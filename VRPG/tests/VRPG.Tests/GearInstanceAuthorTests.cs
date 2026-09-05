using System;
using System.Linq;
using VRPG.Data;
using VRPG.Data.Definitions;
using VRPG.Inventory;
using VRPG.Inventory.Gear;
using Xunit;

namespace VRPG.Tests;

public sealed class GearInstanceAuthorTests
{
    [Fact]
    public void AuthorResolvesExactRollsOnceInRequiredOrder()
    {
        GearAuthoringRequest request = Request(
            baseCode: "vrpg:test-sword",
            level: 40,
            rarity: "vrpg:rare",
            selections: new[]
            {
                new GearAffixSelection("vrpg:flat-power", 2, 0.25f),
                new GearAffixSelection("vrpg:additional-power", 2, 0.50f),
                new GearAffixSelection("vrpg:more-power", 1, 1.00f)
            });

        GearAuthoringResult result = Author().Author(request);

        Assert.True(result.Success, result.Error);
        GearInstance instance = result.Instance!;
        Assert.Equal(40, instance.RequiredLevel);
        Assert.Equal("vrpg:rare", instance.RarityCode);
        Assert.Equal(GearInstanceSchema.CurrentGenerationRules, instance.GenerationRuleVersion);
        Assert.Equal(new[] { "vrpg:flat-power", "vrpg:additional-power", "vrpg:more-power" },
            instance.Affixes.Select(affix => affix.AffixCode));
        Assert.Equal(new[] { "vrpg:weapon_power", "vrpg:max_health" },
            instance.Affixes[0].Modifiers.Select(modifier => modifier.Stat));
        Assert.Equal(new[] { 12f, 3f }, instance.Affixes[0].Modifiers.Select(modifier => modifier.Value));
        Assert.Equal(30f, instance.Affixes[1].Modifiers[0].Value);
        Assert.Equal(20f, instance.Affixes[2].Modifiers[0].Value);
        Assert.Equal(new[] { 20f, 10f, 10f },
            instance.Affixes[2].Modifiers.Select(modifier => modifier.Value));
        Assert.Equal(99.170685f, instance.Resolved.WeaponPower);
        Assert.Equal(24.6675f, instance.Resolved.MaximumHealth);
        Assert.Equal(3f, instance.Resolved.Armor);
        Assert.Equal(7.5f, instance.Resolved.MaximumMagicShield);
        Assert.Equal(0.15f, instance.Resolved.Evasion, 5);
        Assert.Equal(0.15f, instance.Resolved.FireResistance, 5);
        Assert.Equal(0.30f, instance.Resolved.ColdResistance, 5);
        Assert.Equal(0.45f, instance.Resolved.LightningResistance, 5);
        Assert.Equal(0.60f, instance.Resolved.RustResistance, 5);
        Assert.Equal(42UL, instance.Provenance.SelectionSeed);
        Assert.Equal(instance.Resolved.WeaponPower,
            Decode(Prototype(instance).ExactState).Resolved.WeaponPower);
    }

    [Fact]
    public void IdenticalRequestsProduceStableExactState()
    {
        GearAuthoringRequest request = Request(
            "vrpg:test-sword", 40, "vrpg:rare",
            new[]
            {
                new GearAffixSelection("vrpg:flat-power", 2, 0.25f),
                new GearAffixSelection("vrpg:additional-power", 2, 0.50f),
                new GearAffixSelection("vrpg:more-power", 1, 1.00f)
            });

        GearInstance first = Author().Author(request).Instance!;
        GearInstance second = Author().Author(request).Instance!;

        Assert.Equal(Prototype(first).ExactState, Prototype(second).ExactState);
    }

    [Fact]
    public void SelectionSeedIsDiagnosticAndDoesNotChangeExactSelectionsOrRolls()
    {
        GearAuthoringRequest firstRequest = Request(
            "vrpg:test-sword", 20, "vrpg:common",
            new[] { new GearAffixSelection("vrpg:flat-power", 1, 0.75f) });
        GearAuthoringRequest secondRequest = firstRequest with { SelectionSeed = 999UL };

        GearInstance first = Author().Author(firstRequest).Instance!;
        GearInstance second = Author().Author(secondRequest).Instance!;

        Assert.Equal(first.BaseCode, second.BaseCode);
        Assert.Equal(first.RarityCode, second.RarityCode);
        Assert.Equal(first.Affixes, second.Affixes);
        Assert.Equal(first.Resolved, second.Resolved);
        Assert.Equal(42UL, first.Provenance.SelectionSeed);
        Assert.Equal(999UL, second.Provenance.SelectionSeed);
    }

    [Theory]
    [InlineData("missing:base", "vrpg:common", "unknown gear base")]
    [InlineData("vrpg:test-sword", "missing:rarity", "unknown gear rarity")]
    public void AuthorRejectsUnknownBaseOrRarity(string baseCode, string rarityCode, string expected)
    {
        GearAuthoringResult result = Author().Author(Request(baseCode, 1, rarityCode, Array.Empty<GearAffixSelection>()));

        Assert.False(result.Success);
        Assert.Null(result.Instance);
        Assert.Contains(expected, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AuthorRejectsUnknownAffix()
    {
        GearAuthoringResult result = Author().Author(Request(
            "vrpg:test-sword", 1, "vrpg:common",
            new[] { new GearAffixSelection("missing:affix", 1, 0f) }));

        AssertRejected(result, "unknown gear affix");
    }

    [Theory]
    [InlineData("tag")]
    [InlineData("family")]
    [InlineData("role")]
    public void AuthorRejectsAffixIncompatibleWithSelectedBase(string incompatibility)
    {
        VRPGDataRegistry registry = TestGearDefinitions.Registry(data =>
        {
            GearAffixDefinition affix = data.Affixes.Get("vrpg:flat-power")!;
            if (incompatibility == "tag") affix.RequiredTags = new[] { "ranged" };
            if (incompatibility == "family") affix.Family = "defense";
            if (incompatibility == "role") affix.AllowedEquipmentRoles = new[] { PlayerEquipmentRoles.Head };
        });
        GearAuthoringResult result = new GearInstanceAuthor(registry).Author(Request(
            "vrpg:test-sword", 1, "vrpg:common",
            new[] { new GearAffixSelection("vrpg:flat-power", 1, 0f) }));

        AssertRejected(result, incompatibility);
    }

    [Fact]
    public void AuthorRejectsTierAboveRequiredLevel()
    {
        GearAuthoringResult result = Author().Author(Request(
            "vrpg:test-sword", 20, "vrpg:common",
            new[] { new GearAffixSelection("vrpg:flat-power", 2, 0f) }));

        AssertRejected(result, "required level");
    }

    [Fact]
    public void AuthorRejectsDuplicateAffix()
    {
        GearAuthoringResult result = Author().Author(Request(
            "vrpg:test-sword", 20, "vrpg:common",
            new[]
            {
                new GearAffixSelection("vrpg:flat-power", 1, 0f),
                new GearAffixSelection("vrpg:flat-power", 1, 1f)
            }));

        AssertRejected(result, "duplicate");
    }

    [Fact]
    public void AuthorRejectsMutuallyExclusiveAffixes()
    {
        VRPGDataRegistry registry = TestGearDefinitions.Registry(data =>
            data.Affixes.Get("vrpg:additional-power")!.MutualExclusionGroup = "flat-power");
        GearAuthoringResult result = new GearInstanceAuthor(registry).Author(Request(
            "vrpg:test-sword", 40, "vrpg:rare",
            new[]
            {
                new GearAffixSelection("vrpg:flat-power", 2, 0f),
                new GearAffixSelection("vrpg:additional-power", 2, 0f),
                new GearAffixSelection("vrpg:more-power", 1, 0f)
            }));

        AssertRejected(result, "mutual exclusion");
    }

    [Fact]
    public void AuthorRejectsAffixCountOutsideRarityBudget()
    {
        GearAuthoringResult tooMany = Author().Author(Request(
            "vrpg:test-sword", 20, "vrpg:common",
            new[]
            {
                new GearAffixSelection("vrpg:flat-power", 1, 0f),
                new GearAffixSelection("vrpg:additional-power", 1, 0f)
            }));
        GearAuthoringResult tooFew = Author().Author(Request(
            "vrpg:test-sword", 20, "vrpg:rare", Array.Empty<GearAffixSelection>()));

        AssertRejected(tooMany, "affix count");
        AssertRejected(tooFew, "affix count");
    }

    [Fact]
    public void AuthorRejectsUniqueRarityWithoutMatchingBaseIdentity()
    {
        VRPGDataRegistry registry = TestGearDefinitions.Registry(data =>
            data.GearBases.Get("vrpg:test-sword")!.UniqueCode = "");

        GearAuthoringResult result = new GearInstanceAuthor(registry).Author(Request(
            "vrpg:test-sword", 1, "vrpg:unique", Array.Empty<GearAffixSelection>()));

        AssertRejected(result, "unique");
    }

    [Theory]
    [InlineData(-0.01f)]
    [InlineData(1.01f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void AuthorRejectsInvalidRoll(float roll)
    {
        GearAuthoringResult result = Author().Author(Request(
            "vrpg:test-sword", 1, "vrpg:common",
            new[] { new GearAffixSelection("vrpg:flat-power", 1, roll) }));

        AssertRejected(result, "roll01");
    }

    [Fact]
    public void PrototypeUsesFallbackWithoutRewritingEncodedChassis()
    {
        GearInstance original = Author().Author(Request(
            "vrpg:test-sword", 1, "vrpg:common", Array.Empty<GearAffixSelection>())).Instance!;
        GearInstance unavailable = original with { VisualChassisAssetCode = "missing:chassis" };

        PlayerItemPrototype prototype = Prototype(unavailable);
        GearInstance decoded = Decode(prototype.ExactState);

        Assert.Equal("game:gear-rusty", prototype.AssetCode);
        Assert.Equal("missing:chassis", unavailable.VisualChassisAssetCode);
        Assert.Equal("missing:chassis", decoded.VisualChassisAssetCode);
        Assert.Equal(PlayerItemStorageKind.Unique, prototype.Kind);
        Assert.Equal(PlayerItemCategories.Gear, prototype.Category);
        Assert.Equal(PlayerItemAssetKind.Item, prototype.AssetKind);
        Assert.Equal("Test Sword", prototype.DisplayName);
    }

    [Fact]
    public void PrototypeRejectsWhenRequestedChassisAndFallbackAreUnavailable()
    {
        GearInstance original = Author().Author(Request(
            "vrpg:test-sword", 1, "vrpg:common", Array.Empty<GearAffixSelection>())).Instance!;
        GearInstance unavailable = original with { VisualChassisAssetCode = "missing:chassis" };
        var factory = new GearPrototypeFactory(TestGearDefinitions.ChassisExists, "missing:fallback");

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => factory.Create(
            unavailable,
            TestGearDefinitions.RequireBase(unavailable.BaseCode),
            TestGearDefinitions.RequireRarity(unavailable.RarityCode)));

        Assert.Contains("fallback", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrototypeUsesRarityNameOnlyForNonCommonGear()
    {
        GearInstance rare = Author().Author(Request(
            "vrpg:test-sword", 40, "vrpg:rare",
            new[]
            {
                new GearAffixSelection("vrpg:flat-power", 2, 0f),
                new GearAffixSelection("vrpg:additional-power", 2, 0f),
                new GearAffixSelection("vrpg:more-power", 1, 0f)
            })).Instance!;

        Assert.Equal("Rare Test Sword", Prototype(rare).DisplayName);
    }

    private static void AssertRejected(GearAuthoringResult result, string expected)
    {
        Assert.False(result.Success);
        Assert.Null(result.Instance);
        Assert.Contains(expected, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static GearAuthoringRequest Request(
        string baseCode,
        int level,
        string rarity,
        GearAffixSelection[] selections) =>
        new(baseCode, level, rarity, selections, "test", "author", Array.Empty<string>(), 42UL);

    private static GearInstanceAuthor Author() =>
        new(TestGearDefinitions.Registry());

    private static PlayerItemPrototype Prototype(GearInstance instance) =>
        new GearPrototypeFactory(TestGearDefinitions.ChassisExists, "game:gear-rusty").Create(
            instance,
            TestGearDefinitions.RequireBase(instance.BaseCode),
            TestGearDefinitions.RequireRarity(instance.RarityCode));

    private static GearInstance Decode(byte[] state)
    {
        Assert.True(GearItemStateCodec.TryDecode(state, out GearInstance? gear, out string error), error);
        return gear!;
    }
}

internal static class TestGearDefinitions
{
    public static VRPGDataRegistry Registry(Action<VRPGDataRegistry>? afterValidation = null)
    {
        var registry = new VRPGDataRegistry();
        registry.GearBases.Load(new[] { Sword() });
        registry.Rarities.Load(Rarities());
        registry.Affixes.Load(Affixes());
        GearDefinitionValidator.ValidateOrThrow(
            registry.GearBases.All,
            registry.Rarities.All,
            registry.Affixes.All,
            ChassisExists);
        afterValidation?.Invoke(registry);
        return registry;
    }

    public static GearBaseDefinition RequireBase(string code) =>
        Registry().GearBases.Get(code) ?? throw new InvalidOperationException("Missing fixture gear base " + code);

    public static GearRarityDefinition RequireRarity(string code) =>
        Registry().Rarities.Get(code) ?? throw new InvalidOperationException("Missing fixture rarity " + code);

    public static bool ChassisExists(string code) =>
        code is "game:test-sword" or "game:gear-rusty";

    private static GearBaseDefinition Sword() => new()
    {
        Code = "test-sword",
        Name = "Test Sword",
        EquipmentRole = PlayerEquipmentRoles.MainWeapon,
        Family = "sword",
        Tags = new[] { "weapon", "sword" },
        AffixFamilies = new[] { "offense" },
        VisualChassis = "game:test-sword",
        LevelOneWeaponPower = 10f,
        Implicits = new[]
        {
            Modifier("max_health", 10f, 10f, "add"),
            Modifier("armor", 2f, 2f, "add"),
            Modifier("max_magic_shield", 5f, 5f, "add"),
            Modifier("evasion", 0.10f, 0.10f, "add"),
            Modifier("fire_resistance", 0.10f, 0.10f, "add"),
            Modifier("cold_resistance", 0.20f, 0.20f, "add"),
            Modifier("lightning_resistance", 0.30f, 0.30f, "add"),
            Modifier("rust_resistance", 0.40f, 0.40f, "add")
        },
        UniqueCode = "test-sword-unique"
    };

    private static GearRarityDefinition[] Rarities() => new[]
    {
        Rarity("common", "Common", 0, 1, 1f, 1f),
        Rarity("rare", "Rare", 3, 5, 1.5f, 1.15f),
        Rarity("unique", "Unique", 0, 1, 1.75f, 1.20f, uniqueLike: true)
    };

    private static GearRarityDefinition Rarity(
        string code,
        string name,
        int minimum,
        int maximum,
        float statScalar,
        float weaponPowerScalar,
        bool uniqueLike = false) => new()
    {
        Code = code,
        Name = name,
        MinAffixes = minimum,
        MaxAffixes = maximum,
        Weight = 1,
        StatScalar = statScalar,
        WeaponPowerScalar = weaponPowerScalar,
        UniqueLike = uniqueLike
    };

    private static GearAffixDefinition[] Affixes() => new[]
    {
        Affix(
            "flat-power",
            "Flat Power",
            "flat-power",
            Tier(1, 1, 20,
                Modifier("weapon_power", 4f, 8f, "add"),
                Modifier("max_health", 1f, 2f, "add")),
            Tier(2, 21, 100,
                Modifier("weapon_power", 10f, 18f, "add"),
                Modifier("max_health", 2f, 6f, "add"))),
        Affix(
            "additional-power",
            "Additional Power",
            "additional-power",
            Tier(1, 1, 20,
                Modifier("weapon_power", 10f, 20f, "increased"),
                Modifier("max_health", 5f, 10f, "increased")),
            Tier(2, 21, 100,
                Modifier("weapon_power", 20f, 40f, "increased"),
                Modifier("max_health", 10f, 20f, "increased"))),
        Affix(
            "more-power",
            "More Power",
            "more-power",
            Tier(1, 1, 100,
                Modifier("weapon_power", 10f, 20f, "more"),
                Modifier("weapon_power", 10f, 10f, "more"),
                Modifier("max_health", 5f, 10f, "more")))
    };

    private static GearAffixDefinition Affix(
        string code,
        string name,
        string group,
        params GearAffixTierDefinition[] tiers) => new()
    {
        Code = code,
        Name = name,
        Family = "offense",
        RequiredTags = new[] { "weapon" },
        ExcludedTags = Array.Empty<string>(),
        AllowedEquipmentRoles = new[] { PlayerEquipmentRoles.MainWeapon },
        ExcludedEquipmentRoles = Array.Empty<string>(),
        MutualExclusionGroup = group,
        UniquePerItem = true,
        MinimumRarityCode = "common",
        Tiers = tiers
    };

    private static GearAffixTierDefinition Tier(
        int tier,
        int minimumLevel,
        int maximumLevel,
        params StatModifierDefinition[] modifiers) => new()
    {
        Tier = tier,
        MinimumRequiredLevel = minimumLevel,
        MaximumRequiredLevel = maximumLevel,
        Modifiers = modifiers
    };

    private static StatModifierDefinition Modifier(string stat, float min, float max, string operation) => new()
    {
        Stat = stat,
        Min = min,
        Max = max,
        Operation = operation
    };
}
