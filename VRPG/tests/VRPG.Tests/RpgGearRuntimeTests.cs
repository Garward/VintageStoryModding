using System;
using VRPG.Config;
using VRPG.Inventory;
using VRPG.Inventory.Gear;
using VRPG.Modules.Rpg;
using VRPG.Modules.Rpg.Combat;
using VRPG.Modules.Rpg.Players;
using VRPG.Modules.Rpg.Stats;
using VRPG.Modules.Rpg.Talents;
using VRPG.Patches;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Xunit;

namespace VRPG.Tests;

[Collection("EntityDamageScalingPatch")]
public sealed class RpgGearRuntimeTests
{
    [Theory]
    [InlineData(EnumAppSide.Server, false)]
    [InlineData(EnumAppSide.Client, false)]
    [InlineData(EnumAppSide.Server, true)]
    [InlineData(EnumAppSide.Client, true)]
    public void UnreadyMovementCompositionPreservesNativeModifiers(EnumAppSide side, bool persistenceEnabled)
    {
        var fixture = new PlayerDefenseServiceTests.Fixture();
        HarmonyLib.AccessTools.Field(typeof(Vintagestory.Server.ServerWorldPlayerData), "connected")
            .SetValue(fixture.Player.WorldData, true);
        ((Vintagestory.Server.ServerWorldPlayerData)fixture.Player.WorldData).GameMode = EnumGameMode.Survival;
        var system = new VRPGModSystem();
        var config = new VRPGConfig();
        config.Modules.Rpg.InventoryReplacement.Enabled = persistenceEnabled;
        config.Modules.Rpg.CharacterRuntime.BaseWalkSpeedMultiplier = 2;
        HarmonyLib.AccessTools.Field(typeof(VRPGModSystem), "config").SetValue(system, config);
        // Authority composition is the unit under test, not installing unrelated engine hooks.
        HarmonyLib.AccessTools.Field(typeof(VRPGModSystem), "characterRuntimeHarmony")
            .SetValue(system, new HarmonyLib.Harmony("vrpg.tests.movement-readiness"));
        var api = PlayerDefenseServiceTests.Proxy.Create<ICoreAPI>((name, _) =>
            name == "get_Side" ? side : throw new NotSupportedException(name));
        try
        {
            HarmonyLib.AccessTools.Method(typeof(VRPGModSystem), "EnsureCharacterRuntimeAuthority").Invoke(system, new object[] { api });
            Assert.False(SurvivalCharacterRuntimePatch.OwnershipResolver!(fixture.Player.Entity));
            Assert.Null(SurvivalCharacterRuntimePatch.MovementStatResolver!(fixture.Player.Entity, "walkspeed"));
            Assert.Null(SurvivalCharacterRuntimePatch.MovementStatResolver!(fixture.Player.Entity, "jumpHeightMul"));
        }
        finally
        {
            SurvivalCharacterRuntimePatch.OwnershipResolver = null;
            SurvivalCharacterRuntimePatch.MovementStatResolver = null;
            VanillaClassRuntimePatch.OwnershipResolver = null;
            VanillaClassRuntimePatch.ClientAppearanceOnlyResolver = null;
        }
    }

    [Fact]
    public void UnreadyClassSkillPreviewDoesNotAdvertiseFallbackWeaponDamage()
    {
        var data = TestGearDefinitions.Registry();
        data.Classes.Load(new[] { new VRPG.Data.Definitions.ClassDefinition { Code = "test", Name = "Test" } });
        data.Skills.Load(new[] { new VRPG.Data.Definitions.SkillDefinition { Code = "test-hit", ClassCode = "vrpg:test" } });
        using var module = new RpgModule(new RpgModuleConfig(), data, null!);
        var player = new PlayerDefenseServiceTests.Fixture().Player;
        Assert.Equal(0, Assert.Single(Assert.Single(module.BuildClasses(player)).Skills).Damage);
    }

    [Fact]
    public void MissingPersistenceDoesNotOwnPlayersOrCreateAnEmptyGearDirectory()
    {
        using var runtime = new RpgGearRuntime(new RpgModuleConfig(), TestGearDefinitions.Registry(), () => null);
        Assert.False(runtime.ValidateAndRecover(_ => true).Ready);
        Assert.False(runtime.IsReady);
        Assert.False(runtime.ResolveStats("p").Valid);
        Assert.False(runtime.Owns(new PlayerDefenseServiceTests.Fixture().Player.Entity));
    }

    [Fact]
    public void SameRevisionSharesOneSnapshotAndReplacedStoreCannotReturnStaleGear()
    {
        var f = new GearReadinessValidatorTests.Fixture();
        string id = f.Insert(GearItemStateCodec.Encode(GearReadinessValidatorTests.Instance()), PlayerEquipmentRoles.MainWeapon);
        using var runtime = new RpgGearRuntime(new RpgModuleConfig(), f.Data, () => f.Archive);
        Assert.False(runtime.ResolveStats("p").Valid);
        Assert.True(runtime.ValidateAndRecover(_ => true).Ready);
        var original = runtime.ResolveStats("p");
        Assert.Equal(42, original.WeaponPower);
        Assert.Same(original, runtime.ResolveStats("p"));
        runtime.RemovePlayer("p");
        Assert.NotSame(original, runtime.ResolveStats("p"));
        var second = new GearReadinessValidatorTests.Fixture();
        second.Insert(GearItemStateCodec.Encode(GearReadinessValidatorTests.Instance() with
            { Resolved = new(77, 0, 0, 0, 0, 0, 0, 0, 0) }), PlayerEquipmentRoles.MainWeapon);
        Assert.Equal(f.Store.Revision, second.Store.Revision);
        f.Archive.Directory.Set("p", second.Store);
        Assert.Equal(77, runtime.ResolveStats("p").WeaponPower);
        Assert.True(second.Store.Unequip("unequip", PlayerEquipmentRoles.MainWeapon, second.Store.Revision).Success);
        Assert.Equal(10, runtime.ResolveStats("p").AttackWeaponPower);
        Assert.Equal(0, runtime.ResolveStats("p").Armor);
    }

    [Fact]
    public void BoundDamageUsesExactEquipmentAndReleasesAllDelegatesOnDispose()
    {
        var f = new GearReadinessValidatorTests.Fixture();
        f.Insert(GearItemStateCodec.Encode(GearReadinessValidatorTests.Instance()), PlayerEquipmentRoles.MainWeapon);
        var fixture = new PlayerDefenseServiceTests.Fixture();
        HarmonyLib.AccessTools.Field(typeof(Vintagestory.Server.ServerWorldPlayerData), "connected")
            .SetValue(fixture.Player.WorldData, true);
        ((Vintagestory.Server.ServerWorldPlayerData)fixture.Player.WorldData).GameMode = EnumGameMode.Survival;
        using var runtime = new RpgGearRuntime(new RpgModuleConfig(), f.Data, () => f.Archive);
        Assert.True(runtime.ValidateAndRecover(_ => true).Ready);
        var damage = new SkillDamageResolver(new TalentTreeCatalog());
        var weapon = new PlayerWeaponDamageService(runtime.Owns, target => target.WatchedAttributes.GetInt("vrpgLevel") > 0,
            entity => fixture.Store.GetOrCreate(entity.PlayerUID), entity => runtime.ResolveStats(entity.PlayerUID), damage);
        runtime.BindDamageResolvers(weapon.ResolveOutgoing, (_, _, incoming) => incoming * .5f);
        var target = new EntityAgent();
        target.WatchedAttributes.SetInt("vrpgLevel", 1);
        var source = new DamageSource { Source = EnumDamageSource.Player, SourceEntity = fixture.Player.Entity, Type = EnumDamageType.BluntAttack };
        Assert.Equal(42, EntityDamageScalingPatch.PlayerOutgoingDamageResolver!(target, source, 1));
        target.WatchedAttributes.SetInt("vrpgLevel", 0);
        Assert.Null(EntityDamageScalingPatch.PlayerOutgoingDamageResolver!(target, source, 1));
        Assert.True(runtime.Owns(fixture.Player.Entity));
        runtime.Dispose();
        Assert.False(runtime.IsReady);
        Assert.False(runtime.ResolveStats("p").Valid);
        Assert.Null(EntityDamageScalingPatch.PlayerOutgoingDamageResolver);
        Assert.Null(EntityDamageScalingPatch.PlayerDamageResolver);
        Assert.Null(EntityDamageScalingPatch.PlayerDefenseActive);
    }
}
