using System;
using System.Collections.Generic;
using System.Linq;
using VRPG.Data;
using VRPG.Inventory;
using VRPG.Inventory.Gear;
using VRPG.Inventory.Persistence;
using Vintagestory.API.Datastructures;
using Xunit;

namespace VRPG.Tests;

public sealed class GearReadinessValidatorTests
{
    [Theory]
    [InlineData("malformed")]
    [InlineData("base")]
    [InlineData("rarity")]
    [InlineData("affix")]
    [InlineData("schema")]
    [InlineData("operation")]
    [InlineData("modifier")]
    [InlineData("unique-wire")]
    [InlineData("role")]
    [InlineData("equipped-role")]
    [InlineData("fallback")]
    public void InvalidEquippedEntryIsPreservedInDurableRecoveryAndBlocksThisPass(string failure)
    {
        var f = new Fixture();
        byte[] bytes = GearItemStateCodec.Encode(Instance());
        if (failure == "malformed") bytes = new byte[] { 1, 2, 3 };
        else if (failure is "schema" or "operation" or "modifier" or "unique-wire")
        {
            TreeAttribute root = ItemStackCodec.DecodeCanonicalState(bytes);
            ITreeAttribute tree = root.GetTreeAttribute("vrpgGear");
            if (failure == "schema") tree.SetInt("schemaVersion", 99);
            else if (failure == "unique-wire") tree.SetInt("uniqueCode", 0);
            else if (failure == "modifier") ((TreeArrayAttribute)((TreeArrayAttribute)tree["affixes"]).value[0]["modifiers"]).value[0]
                .SetString("stat", "vrpg:unsupported_stat");
            else ((TreeArrayAttribute)((TreeArrayAttribute)tree["affixes"]).value[0]["modifiers"]).value[0]
                .SetString("operation", "unsupported");
            bytes = ItemStackCodec.EncodeCanonicalState(root);
        }
        if (failure == "base") f.Data.GearBases.Load(Array.Empty<VRPG.Data.Definitions.GearBaseDefinition>());
        if (failure == "rarity") f.Data.Rarities.Load(Array.Empty<VRPG.Data.Definitions.GearRarityDefinition>());
        if (failure == "affix") f.Data.Affixes.Load(Array.Empty<VRPG.Data.Definitions.GearAffixDefinition>());
        if (failure == "role") f.Data.GearBases.Get("test-sword")!.EquipmentRole = PlayerEquipmentRoles.Head;
        string id = f.Insert(bytes, failure == "equipped-role" ? PlayerEquipmentRoles.Head : PlayerEquipmentRoles.MainWeapon);
        Assert.True(f.Archive.SaveDirty().Success);

        GearReadinessResult result = GearReadinessValidator.ValidateAndRecover(f.Archive, f.Data,
            _ => failure != "fallback", "game:gear-rusty");

        Assert.False(result.Ready);
        Assert.Equal(1, result.RecoveredEntries);
        Assert.Empty(f.Store.Equipment().Entries);
        PlayerRecoverySummary recovery = Assert.Single(f.Store.Recovery().Entries);
        Assert.Equal(PlayerItemLocation.Unresolved, recovery.Location);
        Assert.Equal(PlayerItemRecoveryReason.UnsupportedContent, recovery.Reason);
        Assert.Equal("gear-readiness", recovery.Source);
        var restarted = f.Restart();
        Assert.True(restarted.Directory.ForPlayer("p").TryGet(id, out var detail));
        Assert.Equal(bytes, detail!.Prototype.ExactState);
        Assert.Equal(PlayerItemLocation.Unresolved, detail.Location);
        Assert.Empty(restarted.Directory.ForPlayer("p").Equipment().Entries);
    }

    [Fact]
    public void StoredGearIsValidatedAndFailedRecoverySaveCannotBecomeReadyOnRetry()
    {
        var f = new Fixture();
        f.Insert(new byte[] { 1, 2, 3 });
        f.Storage.FailWrites = true;
        var result = GearReadinessValidator.ValidateAndRecover(f.Archive, f.Data, _ => true, "game:gear-rusty");
        Assert.False(result.Ready);
        Assert.False(GearReadinessValidator.ValidateAndRecover(f.Archive, f.Data, _ => true, "game:gear-rusty").Ready);
        f.Storage.FailWrites = false;
        Assert.True(GearReadinessValidator.ValidateAndRecover(f.Archive, f.Data, _ => true, "game:gear-rusty").Ready);
        Assert.Single(f.Restart().Directory.ForPlayer("p").Recovery().Entries);
    }

    [Fact]
    public void HealthyRestartKeepsHistoricalRollsAndExactUnknownState()
    {
        var f = new Fixture();
        byte[] bytes = ExactWithExtraState();
        string id = f.Insert(bytes, PlayerEquipmentRoles.MainWeapon);
        Assert.True(f.Archive.SaveDirty().Success);
        var restart = f.Restart();
        // Updated ranges are authoring rules; they must never reroll historical instances.
        f.Data.Affixes.Get("flat-power")!.Tiers[0].Modifiers[0].Max = 1;
        var result = GearReadinessValidator.ValidateAndRecover(restart, f.Data, _ => true, "game:gear-rusty");
        Assert.True(result.Ready, result.Error);
        Assert.Equal(1, result.ValidEntries);
        Assert.Equal(0, result.RecoveredEntries);
        Assert.True(restart.Directory.ForPlayer("p").TryGet(id, out var detail));
        Assert.Equal(bytes, detail!.Prototype.ExactState);
        Assert.Equal(PlayerEquipmentRoles.MainWeapon, Assert.Single(restart.Directory.ForPlayer("p").Equipment().Entries).RoleCode);
    }

    [Fact]
    public void MissingChassisUsesRuntimeFallbackWithoutReencodingStoredState()
    {
        var f = new Fixture();
        byte[] bytes = ExactWithExtraState();
        string id = f.Insert(bytes, PlayerEquipmentRoles.MainWeapon);
        var result = GearReadinessValidator.ValidateAndRecover(f.Archive, f.Data, code => code == "game:gear-rusty", "game:gear-rusty");
        Assert.True(result.Ready, result.Error);
        Assert.True(f.Store.TryGet(id, out var detail));
        PlayerItemPrototype runtime = new GearPrototypeFactory(code => code == "game:gear-rusty", "game:gear-rusty")
            .Materialize(detail!.Prototype);
        Assert.Equal("game:gear-rusty", runtime.AssetCode);
        Assert.Equal(bytes, runtime.ExactState);
        Assert.Equal("game:test-sword", detail.Prototype.AssetCode);
        Assert.Equal(bytes, detail.Prototype.ExactState);
        Assert.True(GearItemStateCodec.TryDecode(runtime.ExactState, out var decoded, out _));
        Assert.Equal("game:test-sword", decoded!.VisualChassisAssetCode);
        runtime.ExactState[0] ^= 0xff;
        Assert.Equal(bytes, detail.Prototype.ExactState);
    }

    [Fact]
    public void UnloadedArchiveNeverCreatesPlayerStoresOrReportsReady()
    {
        var archive = new PlayerItemArchiveService(new PlayerItemArchiveRepository(new MemoryStorage()));
        Assert.False(GearReadinessValidator.ValidateAndRecover(archive, TestGearDefinitions.Registry(), _ => true, "game:gear-rusty").Ready);
        Assert.Empty(archive.Directory.Snapshot());
    }

    internal static GearInstance Instance() => new(1, 1, "vrpg:test-sword", 20, "vrpg:common",
        PlayerEquipmentRoles.MainWeapon, "game:test-sword",
        new[] { new GearAffixRoll("vrpg:flat-power", 1, new[] { new GearModifierRoll("vrpg:weapon_power", "add", 6.5f) }) },
        new(42, 150, 25, 8, .2f, .8f, .8f, .8f, .8f), new("test", "restart", Array.Empty<string>(), 123), "", Array.Empty<byte>());

    private static byte[] ExactWithExtraState()
    {
        TreeAttribute root = ItemStackCodec.DecodeCanonicalState(GearItemStateCodec.Encode(Instance()));
        root.SetString("other-mod-state", "preserve me exactly");
        return ItemStackCodec.EncodeCanonicalState(root);
    }

    internal sealed class Fixture
    {
        public readonly MemoryStorage Storage = new();
        public readonly VRPGDataRegistry Data = TestGearDefinitions.Registry();
        public readonly PlayerItemArchiveService Archive;
        public IPlayerItemStore Store => Archive.Directory.ForPlayer("p");
        public Fixture() { Archive = Restart(); }
        public PlayerItemArchiveService Restart()
        {
            var result = new PlayerItemArchiveService(new PlayerItemArchiveRepository(Storage));
            Assert.True(result.Load().Success);
            return result;
        }
        public string Insert(byte[] bytes, string? role = null)
        {
            var result = Store.Insert(Guid.NewGuid().ToString(), new(PlayerItemStorageKind.Unique,
                "game:test-sword", PlayerItemCategories.Gear, "Test Sword", bytes), 1, Store.Revision);
            Assert.True(result.Success);
            if (role != null) Assert.True(Store.Equip(Guid.NewGuid().ToString(), result.EntryId, role, Store.Revision).Success);
            return result.EntryId;
        }
    }

    internal sealed class MemoryStorage : IPlayerItemArchiveStorage
    {
        private readonly Dictionary<string, byte[]> values = new();
        public bool FailWrites;
        public byte[]? Read(string key) => values.TryGetValue(key, out var bytes) ? (byte[])bytes.Clone() : null;
        public void Write(string key, byte[] bytes)
        {
            if (FailWrites) throw new InvalidOperationException("Simulated unavailable persistence");
            values[key] = (byte[])bytes.Clone();
        }
    }
}
