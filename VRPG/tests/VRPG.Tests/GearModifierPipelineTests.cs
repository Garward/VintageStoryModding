using System;
using System.Linq;
using Newtonsoft.Json;
using VRPG.Config;
using VRPG.Data;
using VRPG.Data.Definitions;
using VRPG.Inventory;
using VRPG.Inventory.Gear;
using VRPG.Modules.Rpg.Combat;
using VRPG.Modules.Rpg.Players;
using VRPG.Modules.Rpg.Stats;
using VRPG.Modules.Rpg.Talents;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Xunit;

namespace VRPG.Tests;

public sealed class GearModifierPipelineTests
{
    [Theory]
    [InlineData("vrpg:strength", 190f, 100f, 8.2f, 10.36f)]
    [InlineData("vrpg:intelligence", 100f, 460f, 1f, 10.72f)]
    [InlineData("vrpg:dexterity", 100f, 100f, 1f, 10.72f)]
    public void CoreAttributesResolveFlatIncreasedAndMoreBeforeEveryConsumer(string stat, float health, float mana,
        float healthRegen, float damageValue)
    {
        VRPGDataRegistry data = StarterGearSetFactoryTests.Registry();
        data.Affixes.Get("vrpg:heavy_handed")!.Tiers[0].Modifiers = new[]
        {
            Modifier(stat, "add", 2), Modifier(stat, "increased", 50), Modifier(stat, "more", 100)
        };
        GearDefinitionValidator.ValidateOrThrow(data.GearBases.All, data.Rarities.All, data.Affixes.All,
            StarterGearSetFactoryTests.ChassisExists);
        var equipment = new GearEquipmentService(data);
        var store = new InMemoryPlayerItemStore();
        AuthorAndEquip(data, store, equipment, "vrpg:smith-starter-weapon", "vrpg:heavy_handed");
        EquippedGearStats gear = Resolve(store, equipment, new EquippedGearStatResolver());
        var state = new RpgPlayerState();
        state.BaseStats[stat] = 10;
        state.BaseStats[stat.Substring(5)] = 999; // A legacy alias must not override the canonical allocation.
        string before = JsonConvert.SerializeObject(state);
        var talents = new TalentTreeCatalog();
        var resources = new RpgResourceCalculator(new RpgModuleConfig(), talents);
        // Each core attribute resolves (10 + 2) * 1.5 * 2 = 36, without rounding to an allocated int.
        Assert.Equal(health, resources.CalculateMaximums(state, gear).Health, 4);
        Assert.Equal(mana, resources.CalculateMaximums(state, gear).Mana, 4);
        Assert.Equal(healthRegen, resources.CalculateRegeneration(state, gear).Health, 4);
        var skill = new SkillDefinition { Tags = new[] { "spell", "projectile" } };
        Assert.Equal(damageValue, new SkillDamageResolver(talents).Resolve(state, skill, 1, gear), 4);
        Assert.Equal(before, JsonConvert.SerializeObject(state));
    }

    [Fact]
    public void ShippedStalwartAddsHealthPercentageAlongsideIncreasedAndBeforeMore()
    {
        VRPGDataRegistry data = StarterGearSetFactoryTests.Registry();
        var equipment = new GearEquipmentService(data);
        var store = new InMemoryPlayerItemStore();
        var resolver = new EquippedGearStatResolver();
        string id = AuthorAndEquip(data, store, equipment, "vrpg:smith-starter-body", "vrpg:stalwart");
        var state = new RpgPlayerState();
        state.BaseStats["vrpg:strength"] = 10;
        state.Talents.Add("vrpg:health");
        var talents = new TalentTreeCatalog();
        talents.Replace("test", "Test", new[] { new TalentNodeDefinition
        {
            Code = "vrpg:health", Modifiers = new[]
            {
                Modifier("max_health", "increased", 20), Modifier("max_health", "more", 50)
            }
        } });
        var resources = new RpgResourceCalculator(new RpgModuleConfig(), talents);
        EquippedGearStats gear = Resolve(store, equipment, resolver);

        // (100 + 10 * 2.5) * (1 + (20 + 8) / 100) * 1.5 = 240.
        Assert.Equal(240, resources.CalculateMaximums(state, gear).Health, 4);
        Assert.Equal(240, resources.CalculateMaximums(state, gear).Blood, 4);
        Assert.Equal(3, resources.CalculateRegeneration(state, gear).Health, 4);
        Assert.True(store.Unequip("remove", PlayerEquipmentRoles.Body, store.Revision).Success);
        Assert.Equal(225, resources.CalculateMaximums(state, Resolve(store, equipment, resolver)).Health, 4);
        Assert.True(store.TryGet(id, out var detail));
        Assert.True(GearItemStateCodec.TryDecode(detail!.Prototype.ExactState, out var saved, out _));
        Assert.Equal(new GearModifierRoll("vrpg:max_health_percent", "add", 8), Assert.Single(saved!.Affixes[0].Modifiers));
    }

    [Fact]
    public void ShippedHeavyHandedUsesEffectiveStrengthForResourcesSkillsAndBasicAttacksWithoutChangingBaseStats()
    {
        VRPGDataRegistry data = StarterGearSetFactoryTests.Registry();
        var equipment = new GearEquipmentService(data);
        var store = new InMemoryPlayerItemStore();
        var resolver = new EquippedGearStatResolver();
        AuthorAndEquip(data, store, equipment, "vrpg:smith-starter-weapon", "vrpg:heavy_handed");
        var state = new RpgPlayerState();
        state.BaseStats["vrpg:strength"] = 10;
        string before = JsonConvert.SerializeObject(state);
        var talents = new TalentTreeCatalog();
        var resources = new RpgResourceCalculator(new RpgModuleConfig(), talents);
        var damage = new SkillDamageResolver(talents);
        EquippedGearStats gear = Resolve(store, equipment, resolver);
        var skill = new SkillDefinition { Damage = new SkillDamageDefinition { WeaponDamagePercent = 200 } };

        Assert.Equal(132.5f, resources.CalculateMaximums(state, gear).Health);
        Assert.Equal(132.5f, resources.CalculateMaximums(state, gear).Blood);
        Assert.Equal(3.6f, resources.CalculateRegeneration(state, gear).Health, 4);
        Assert.Equal(24.312f, damage.Resolve(state, skill, 1, gear), 4); // (10 * 2 + 4) * 1.013
        var player = new EntityPlayer();
        var attacks = new PlayerWeaponDamageService(_ => true, _ => true, _ => state, _ => gear, damage);
        var source = new DamageSource
        {
            Source = EnumDamageSource.Player, SourceEntity = player, CauseEntity = player,
            Type = EnumDamageType.BluntAttack
        };
        Assert.Equal(14.182f, attacks.Resolve(new EntityAgent(), source, 999), 4); // (10 + 4) * 1.013
        Assert.Equal(before, JsonConvert.SerializeObject(state));

        Assert.True(store.Unequip("remove", PlayerEquipmentRoles.MainWeapon, store.Revision).Success);
        gear = Resolve(store, equipment, resolver);
        Assert.Equal(125, resources.CalculateMaximums(state, gear).Health);
        Assert.Equal(3, resources.CalculateRegeneration(state, gear).Health, 4);
        Assert.Equal(20.2f, damage.Resolve(state, skill, 1, gear), 4);
        Assert.Equal(10.1f, attacks.Resolve(new EntityAgent(), source, 999), 4);
        Assert.Equal(before, JsonConvert.SerializeObject(state));
    }

    [Theory]
    [InlineData("vrpg:strength")]
    [InlineData("vrpg:max_health_percent")]
    [InlineData("vrpg:max_mana")]
    [InlineData("vrpg:physical_damage")]
    public void NonBakedBaseImplicitsAreRejectedBeforeTheyCanDisappear(string stat)
    {
        VRPGDataRegistry data = StarterGearSetFactoryTests.Registry();
        data.GearBases.Get("vrpg:smith-starter-body")!.Implicits = new[] { Modifier(stat, "add", 8) };
        string[] errors = GearDefinitionValidator.Validate(data.GearBases.All, data.Rarities.All,
            data.Affixes.All, StarterGearSetFactoryTests.ChassisExists);
        Assert.Contains(errors, error => error.Contains("implicit", StringComparison.OrdinalIgnoreCase)
            && error.Contains("resolved", StringComparison.OrdinalIgnoreCase));
        GearAuthoringResult result = new GearInstanceAuthor(data).Author(Request("vrpg:smith-starter-body"));
        Assert.False(result.Success);
        Assert.Null(result.Instance);
        Assert.Contains("implicit", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BakedBaseImplicitSurvivesAuthoringCodecAndEquipmentWithoutCurrentDefinitionRecalculation()
    {
        VRPGDataRegistry data = StarterGearSetFactoryTests.Registry();
        data.GearBases.Get("vrpg:smith-starter-body")!.Implicits = new[] { Modifier("vrpg:max_health", "add", 8) };
        GearDefinitionValidator.ValidateOrThrow(data.GearBases.All, data.Rarities.All, data.Affixes.All,
            StarterGearSetFactoryTests.ChassisExists);
        var equipment = new GearEquipmentService(data);
        var store = new InMemoryPlayerItemStore();
        AuthorAndEquip(data, store, equipment, "vrpg:smith-starter-body");
        data.GearBases.Get("vrpg:smith-starter-body")!.Implicits[0].Min = 999;
        EquippedGearStats gear = Resolve(store, equipment, new EquippedGearStatResolver());
        Assert.Equal(108, new RpgResourceCalculator(new RpgModuleConfig(), new TalentTreeCatalog())
            .CalculateMaximums(new RpgPlayerState(), gear).Health);
    }

    [Theory]
    [InlineData("vrpg:unknown_stat", "add")]
    [InlineData("other:max_mana", "add")]
    [InlineData("vrpg:max_health_percent", "more")]
    [InlineData("vrpg:max_health_percent", "increased")]
    public void UnsupportedAffixContractIsRejectedByValidationAuthoringAndEquipment(string stat, string operation)
    {
        VRPGDataRegistry data = StarterGearSetFactoryTests.Registry();
        data.Affixes.Get("vrpg:stalwart")!.Tiers[0].Modifiers = new[] { Modifier(stat, operation, 8) };
        string[] errors = GearDefinitionValidator.Validate(data.GearBases.All, data.Rarities.All,
            data.Affixes.All, StarterGearSetFactoryTests.ChassisExists);
        Assert.Contains(errors, error => error.Contains("unsupported", StringComparison.OrdinalIgnoreCase));
        GearAuthoringResult result = new GearInstanceAuthor(data).Author(Request("vrpg:smith-starter-body", "vrpg:stalwart"));
        Assert.False(result.Success);
        Assert.Null(result.Instance);

        // The wire codec preserves historical identities; unsupported mechanics cannot be equipped.
        GearInstance saved = EquippedGearStatResolverTests.Gear() with
        {
            BaseCode = "vrpg:smith-starter-body", RarityCode = "vrpg:common", EquipmentRole = PlayerEquipmentRoles.Body,
            Affixes = new[] { new GearAffixRoll("vrpg:stalwart", 1, new[] { new GearModifierRoll(stat, operation, 8) }) }
        };
        byte[] bytes = GearItemStateCodec.Encode(saved);
        Assert.True(GearItemStateCodec.TryDecode(bytes, out var decoded, out _));
        Assert.Equal(bytes, GearItemStateCodec.Encode(decoded!));
        var store = new InMemoryPlayerItemStore();
        PlayerItemMutationResult insert = store.Insert("insert", new(PlayerItemStorageKind.Unique,
            "game:test", PlayerItemCategories.Gear, "Test", bytes), 1, store.Revision);
        Assert.True(insert.Success);
        var equipment = new GearEquipmentService(data);
        Assert.Equal(PlayerItemOperationRejection.InvalidGear,
            equipment.TryEquip(store, 1, "equip", insert.EntryId, PlayerEquipmentRoles.Body, store.Revision).Rejection);
        var snapshot = new GearEquipmentSnapshot(1, true,
            new[] { new GearEquipmentEntry(saved.EquipmentRole, "entry", saved) }, "");
        Assert.False(new EquippedGearStatResolver().Resolve("p", 1, snapshot).Valid);
    }

    private static StatModifierDefinition Modifier(string stat, string operation, float value) =>
        new() { Stat = stat, Operation = operation, Min = value, Max = value };

    private static GearAuthoringRequest Request(string baseCode, string? affixCode = null) =>
        new(baseCode, 1, "vrpg:common", affixCode == null ? Array.Empty<GearAffixSelection>()
            : new[] { new GearAffixSelection(affixCode, 1, 1) }, "test", "pipeline", Array.Empty<string>(), 42);

    private static string AuthorAndEquip(VRPGDataRegistry data, InMemoryPlayerItemStore store,
        GearEquipmentService equipment, string baseCode, string? affixCode = null)
    {
        GearAuthoringResult authored = new GearInstanceAuthor(data).Author(Request(baseCode, affixCode));
        Assert.True(authored.Success, authored.Error);
        GearInstance instance = authored.Instance!;
        PlayerItemPrototype prototype = new GearPrototypeFactory(StarterGearSetFactoryTests.ChassisExists, "game:gear-rusty")
            .Create(instance, data.GearBases.Get(instance.BaseCode)!, data.Rarities.Get(instance.RarityCode)!);
        Assert.True(GearItemStateCodec.TryDecode(prototype.ExactState, out var decoded, out var error), error);
        Assert.Equal(instance.Affixes, decoded!.Affixes);
        PlayerItemMutationResult inserted = store.Insert("insert-" + baseCode, prototype, 1, store.Revision);
        Assert.True(inserted.Success);
        Assert.True(equipment.TryEquip(store, 1, "equip-" + baseCode, inserted.EntryId, instance.EquipmentRole, store.Revision).Success);
        return inserted.EntryId;
    }

    private static EquippedGearStats Resolve(InMemoryPlayerItemStore store, GearEquipmentService equipment,
        EquippedGearStatResolver resolver)
    {
        GearEquipmentSnapshot snapshot = equipment.Snapshot(store);
        Assert.True(snapshot.Valid, snapshot.Error);
        EquippedGearStats result = resolver.Resolve("p", store.Revision, snapshot);
        Assert.True(result.Valid);
        return result;
    }
}
