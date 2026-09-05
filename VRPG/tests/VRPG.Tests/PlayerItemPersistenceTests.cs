using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using VRPG.Inventory;
using VRPG.Inventory.Gear;
using VRPG.Inventory.Persistence;
using Xunit;

namespace VRPG.Tests;

public sealed class PlayerItemPersistenceTests
{
    [Fact]
    public void CompleteStoreRoundTripsAcrossServerRestart()
    {
        var storage = new MemoryArchiveStorage();
        PlayerItemArchiveService firstService = Service(storage);
        Assert.True(firstService.Load().Success);
        var store = Assert.IsType<InMemoryPlayerItemStore>(
            firstService.Directory.ForPlayer("player-one"));
        PlayerItemPrototype material = Material() with
        {
            EmbeddedReferences = new[]
            {
                new CanonicalCollectibleReference(PlayerItemAssetKind.Item, 7, "addon:nested")
            }
        };
        PlayerItemMutationResult materialInsert = store.Insert("insert-material", material, 10, 0);
        GearInstance expectedGear = GearInstanceFixture();
        PlayerItemPrototype expectedGearPrototype = Gear(expectedGear);
        PlayerItemMutationResult gearInsert = store.Insert(
            "insert-gear",
            expectedGearPrototype,
            1,
            materialInsert.Revision);
        PlayerItemMutationResult missingInsert = store.Insert(
            "insert-missing",
            Material("missing:component"),
            4,
            gearInsert.Revision);
        PlayerItemOperationResult equipped = new GearEquipmentService(TestGearDefinitions.Registry()).TryEquip(
            store,
            expectedGear.RequiredLevel,
            "equip",
            gearInsert.EntryId,
            PlayerEquipmentRoles.MainWeapon,
            missingInsert.Revision);
        PlayerItemOperationResult unresolved = store.MoveToRecovery(
            "unresolved",
            missingInsert.EntryId,
            PlayerItemLocation.Unresolved,
            PlayerItemRecoveryReason.MissingAsset,
            "addon reload",
            equipped.Revision);
        PlayerItemReservationResult reserved = store.Reserve(
            "reserve",
            new[] { new PlayerItemAmount(materialInsert.EntryId, 3) },
            unresolved.Revision);
        PlayerItemMutationResult rejected = store.Extract(
            "rejected-extract",
            materialInsert.EntryId,
            99,
            reserved.Revision);
        Assert.False(rejected.Success);

        PlayerItemArchiveSaveResult saved = firstService.SaveDirty();
        Assert.True(saved.Success);
        Assert.False(store.IsDirty);

        PlayerItemArchiveService restoredService = Service(storage);
        PlayerItemArchiveLoadResult loaded = restoredService.Load();
        var restored = Assert.IsType<InMemoryPlayerItemStore>(
            restoredService.Directory.ForPlayer("PLAYER-ONE"));

        Assert.True(loaded.Success);
        Assert.Equal(saved.Generation, loaded.Generation);
        Assert.False(restored.IsDirty);
        EquippedPlayerItemRecord restoredEquipment = restored.Equipment().Entries.Single();
        Assert.Equal(gearInsert.EntryId, restoredEquipment.EntryId);
        Assert.Equal(PlayerEquipmentRoles.MainWeapon, restoredEquipment.RoleCode);
        Assert.True(restored.TryGet(gearInsert.EntryId, out PlayerItemDetail? restoredGearDetail));
        Assert.Equal(expectedGearPrototype.ExactState, restoredGearDetail!.Prototype.ExactState);
        Assert.True(GearItemStateCodec.TryDecode(
            restoredGearDetail.Prototype.ExactState,
            out GearInstance? restoredGear,
            out string gearError), gearError);
        AssertGearContentEqual(expectedGear, restoredGear!);
        PlayerItemOperationResult unequippedGear = restored.Unequip(
            "unequip-restored-gear",
            PlayerEquipmentRoles.MainWeapon,
            restored.Revision);
        PlayerItemMutationResult extractedGear = restored.Extract(
            "extract-restored-gear",
            gearInsert.EntryId,
            1,
            unequippedGear.Revision);
        PlayerItemOperationResult persistedEquipReplay =
            new GearEquipmentService(TestGearDefinitions.Registry()).TryEquip(
                restored,
                expectedGear.RequiredLevel,
                "equip",
                gearInsert.EntryId,
                PlayerEquipmentRoles.MainWeapon,
                missingInsert.Revision);
        PlayerItemOperationResult persistedEquipConflict =
            new GearEquipmentService(TestGearDefinitions.Registry()).TryEquip(
                restored,
                expectedGear.RequiredLevel,
                "equip",
                "missing",
                PlayerEquipmentRoles.Head,
                extractedGear.Revision);
        Assert.True(extractedGear.Success);
        Assert.Equal(equipped, persistedEquipReplay);
        Assert.Equal(PlayerItemOperationRejection.RequestIdConflict, persistedEquipConflict.Rejection);
        PlayerRecoverySummary recovery = restored.Recovery().Entries.Single();
        Assert.Equal(PlayerItemRecoveryReason.MissingAsset, recovery.Reason);
        Assert.Equal("addon reload", recovery.Source);
        Assert.Equal(3, Entry(restored, materialInsert.EntryId).ReservedQuantity);
        PlayerItemReservationSnapshot reservation = restored.Reservation(reserved.ReservationId);
        Assert.Equal(new byte[] { 1, 2, 3 }, reservation.Entries[0].Prototype.ExactState);
        Assert.Equal("addon:nested", reservation.Entries[0].Prototype.EmbeddedReferences![0].AssetCode);

        PlayerItemMutationResult replay = restored.Insert("insert-material", material, 10, 0);
        PlayerItemMutationResult rejectedReplay = restored.Extract(
            "rejected-extract",
            materialInsert.EntryId,
            99,
            reserved.Revision);
        Assert.Equal(materialInsert, replay);
        Assert.Equal(rejected, rejectedReplay);
        Assert.Equal(10, Entry(restored, materialInsert.EntryId).Quantity);
        Assert.True(restored.CommitReservation("commit-after-restart", reserved.ReservationId).Success);
    }

    [Fact]
    public void CorruptNewestGenerationFallsBackToPreviousSnapshot()
    {
        var storage = new MemoryArchiveStorage();
        var repository = new PlayerItemArchiveRepository(storage);
        var first = new PlayerItemArchive();
        first.ExtensionData = new Dictionary<string, JToken>
        {
            ["label"] = JValue.CreateString("first")
        };
        Assert.True(repository.Save(first).Success);
        var second = new PlayerItemArchive();
        second.ExtensionData = new Dictionary<string, JToken>
        {
            ["label"] = JValue.CreateString("second")
        };
        Assert.True(repository.Save(second).Success);
        storage.Corrupt(PlayerItemArchiveRepository.SlotBKey);

        PlayerItemArchiveLoadResult loaded = repository.Load();

        Assert.True(loaded.Success);
        Assert.True(loaded.RecoveredFromOlderGeneration);
        Assert.Equal(1, loaded.Generation);
        Assert.Equal("first", (string?)loaded.Archive.ExtensionData!["label"]);
    }

    [Fact]
    public void InterruptedWriteLeavesPriorGenerationRecoverable()
    {
        var storage = new MemoryArchiveStorage();
        var repository = new PlayerItemArchiveRepository(storage);
        Assert.True(repository.Save(new PlayerItemArchive()).Success);
        byte[] prior = storage.Read(PlayerItemArchiveRepository.SlotAKey)!;
        storage.FailNextWrite = true;

        PlayerItemArchiveSaveResult failed = repository.Save(new PlayerItemArchive());
        PlayerItemArchiveLoadResult loaded = repository.Load();

        Assert.False(failed.Success);
        Assert.True(loaded.Success);
        Assert.Equal(1, loaded.Generation);
        Assert.Equal(prior, storage.Read(PlayerItemArchiveRepository.SlotAKey));
    }

    [Fact]
    public void CorruptionOfEveryGenerationRefusesActivation()
    {
        var storage = new MemoryArchiveStorage();
        var repository = new PlayerItemArchiveRepository(storage);
        Assert.True(repository.Save(new PlayerItemArchive()).Success);
        Assert.True(repository.Save(new PlayerItemArchive()).Success);
        storage.Corrupt(PlayerItemArchiveRepository.SlotAKey);
        storage.Corrupt(PlayerItemArchiveRepository.SlotBKey);
        var service = new PlayerItemArchiveService(repository);

        PlayerItemArchiveLoadResult loaded = service.Load();

        Assert.False(loaded.Success);
        Assert.False(service.IsLoaded);
    }

    [Fact]
    public void UnversionedArchiveMigratesAndPreservesUnknownFields()
    {
        var serializer = new PlayerItemArchiveSerializer();
        var archive = new PlayerItemArchive
        {
            SchemaVersion = 0,
            ExtensionData = new Dictionary<string, JToken>
            {
                ["futureRoot"] = JObject.Parse("{\"enabled\":true}")
            },
            Players = new Dictionary<string, PlayerItemStoreState>
            {
                ["player"] = new PlayerItemStoreState
                {
                    ExtensionData = new Dictionary<string, JToken>
                    {
                        ["futurePlayer"] = new JArray(1, 2, 3)
                    }
                }
            }
        };

        byte[] bytes = serializer.Serialize(archive);
        Assert.True(serializer.TryDeserialize(bytes, out PlayerItemArchive migrated, out string error), error);
        byte[] rewritten = serializer.Serialize(migrated);
        Assert.True(serializer.TryDeserialize(rewritten, out PlayerItemArchive reloaded, out error), error);

        Assert.Equal(PlayerItemPersistenceSchema.CurrentVersion, reloaded.SchemaVersion);
        Assert.True((bool)reloaded.ExtensionData!["futureRoot"]["enabled"]!);
        Assert.Equal(3, reloaded.Players["player"].ExtensionData!["futurePlayer"].Count());
    }

    [Fact]
    public void FutureSchemaIsRejectedInsteadOfSilentlyDowngraded()
    {
        var serializer = new PlayerItemArchiveSerializer();
        byte[] bytes = serializer.Serialize(new PlayerItemArchive
        {
            SchemaVersion = PlayerItemPersistenceSchema.CurrentVersion + 1
        });

        bool success = serializer.TryDeserialize(bytes, out _, out string error);

        Assert.False(success);
        Assert.Contains("Unsupported", error);
    }

    [Fact]
    public void NewerIncompatibleGenerationBlocksFallbackAndOverwrite()
    {
        var storage = new MemoryArchiveStorage();
        var repository = new PlayerItemArchiveRepository(storage);
        Assert.True(repository.Save(new PlayerItemArchive()).Success);
        Assert.True(repository.Save(new PlayerItemArchive
        {
            SchemaVersion = PlayerItemPersistenceSchema.CurrentVersion + 1
        }).Success == false);

        // Install a checksummed future document to model opening a save with an older mod build.
        var serializer = new PlayerItemArchiveSerializer();
        byte[] future = serializer.Serialize(new PlayerItemArchive
        {
            SchemaVersion = PlayerItemPersistenceSchema.CurrentVersion + 1
        });
        storage.Write(
            PlayerItemArchiveRepository.SlotBKey,
            PlayerItemArchiveSlotCodec.Encode(2, future));
        int writes = storage.WriteCount;

        PlayerItemArchiveLoadResult loaded = repository.Load();
        PlayerItemArchiveSaveResult saved = repository.Save(new PlayerItemArchive());

        Assert.False(loaded.Success);
        Assert.False(saved.Success);
        Assert.Equal(writes, storage.WriteCount);
        Assert.Contains("Unsupported", loaded.Error);
    }

    [Fact]
    public void UnknownPerPlayerFieldsSurviveDirtyStoreRewrite()
    {
        var storage = new MemoryArchiveStorage();
        var repository = new PlayerItemArchiveRepository(storage);
        var seedStore = new InMemoryPlayerItemStore(() => "entry");
        seedStore.Insert(Material(), 1, 0);
        PlayerItemStoreState state = seedStore.Capture().State;
        state.ExtensionData = new Dictionary<string, JToken>
        {
            ["futurePlayer"] = new JValue("kept")
        };
        state.Entries[0].ExtensionData = new Dictionary<string, JToken>
        {
            ["futureEntry"] = new JValue(42)
        };
        Assert.True(repository.Save(new PlayerItemArchive
        {
            Players = new Dictionary<string, PlayerItemStoreState> { ["player"] = state }
        }).Success);
        var service = new PlayerItemArchiveService(repository);
        Assert.True(service.Load().Success);
        var restored = Assert.IsType<InMemoryPlayerItemStore>(service.Directory.ForPlayer("player"));
        restored.SetFlags("favorite", "entry", PlayerItemFlags.Favorite, restored.Revision);

        Assert.True(service.SaveDirty().Success);
        PlayerItemStoreState rewritten = repository.Load().Archive.Players["player"];

        Assert.Equal("kept", (string?)rewritten.ExtensionData!["futurePlayer"]);
        Assert.Equal(42, (int)rewritten.Entries[0].ExtensionData!["futureEntry"]);
    }

    [Fact]
    public void CleanSaveDoesNotWriteAnotherGeneration()
    {
        var storage = new MemoryArchiveStorage();
        PlayerItemArchiveService service = Service(storage);
        Assert.True(service.Load().Success);
        Assert.True(service.SaveDirty().Success);
        int writes = storage.WriteCount;

        PlayerItemArchiveSaveResult unchanged = service.SaveDirty();

        Assert.True(unchanged.Success);
        Assert.Equal(writes, storage.WriteCount);
        Assert.Equal(1, unchanged.Generation);
    }

    [Fact]
    public void MutationRacingSnapshotRemainsDirtyForNextSave()
    {
        var storage = new MemoryArchiveStorage();
        PlayerItemArchiveService service = Service(storage);
        Assert.True(service.Load().Success);
        var store = Assert.IsType<InMemoryPlayerItemStore>(service.Directory.ForPlayer("player"));
        PlayerItemMutationResult inserted = store.Insert("insert", Material(), 2, 0);
        storage.BeforeWrite = () => store.SetFlags(
            "favorite",
            inserted.EntryId,
            PlayerItemFlags.Favorite,
            inserted.Revision);

        Assert.True(service.SaveDirty().Success);

        Assert.True(store.IsDirty);
        storage.BeforeWrite = null;
        Assert.True(service.SaveDirty().Success);
        Assert.False(store.IsDirty);
    }

    [Fact]
    public void InvalidPersistedReferencesRejectTheWholeLoadWithoutPartialStores()
    {
        var storage = new MemoryArchiveStorage();
        var archive = new PlayerItemArchive
        {
            Players = new Dictionary<string, PlayerItemStoreState>
            {
                ["valid"] = new PlayerItemStoreState(),
                ["invalid"] = new PlayerItemStoreState
                {
                    Revision = 1,
                    Entries = new[]
                    {
                        new PlayerItemEntryState
                        {
                            EntryId = "bad",
                            Prototype = Material(),
                            Quantity = 1,
                            Location = PlayerItemLocation.Equipped
                        }
                    }
                }
            }
        };
        Assert.True(new PlayerItemArchiveRepository(storage).Save(archive).Success);
        PlayerItemArchiveService service = Service(storage);

        PlayerItemArchiveLoadResult loaded = service.Load();

        Assert.False(loaded.Success);
        Assert.False(service.IsLoaded);
        Assert.Empty(service.Directory.Snapshot());
    }

    private static PlayerItemArchiveService Service(MemoryArchiveStorage storage) =>
        new(new PlayerItemArchiveRepository(storage));

    private static PlayerItemSummary Entry(InMemoryPlayerItemStore store, string entryId)
    {
        foreach (PlayerItemLocation location in Enum.GetValues<PlayerItemLocation>())
        {
            PlayerItemSummary? entry = store.Query(new PlayerItemQuery("", "", 0, 100, location))
                .Entries.FirstOrDefault(candidate => candidate.EntryId == entryId);
            if (entry != null) return entry;
        }
        throw new InvalidOperationException("Entry not found: " + entryId);
    }

    private static PlayerItemPrototype Material(string code = "game:copper") =>
        new(
            PlayerItemStorageKind.Aggregate,
            code,
            PlayerItemCategories.Materials,
            "Copper",
            new byte[] { 1, 2, 3 });

    private static PlayerItemPrototype Gear(GearInstance gear) =>
        new(
            PlayerItemStorageKind.Unique,
            "game:sword",
            PlayerItemCategories.Gear,
            "Sword",
            GearItemStateCodec.Encode(gear));

    private static GearInstance GearInstanceFixture() => new(
        GearInstanceSchema.CurrentVersion,
        GearInstanceSchema.CurrentGenerationRules,
        "vrpg:test-sword",
        20,
        "vrpg:common",
        PlayerEquipmentRoles.MainWeapon,
        "game:test-sword",
        new[]
        {
            new GearAffixRoll("vrpg:flat-power", 1, new[]
            {
                new GearModifierRoll("vrpg:weapon_power", GearModifierOperations.Add, 6.5f)
            })
        },
        new GearResolvedStats(21.5f, 2, 10, 5, 0.1f, 0.1f, 0.2f, 0.3f, 0.4f),
        new GearProvenance("test", "persistence", new[] { "restart", "exact" }, 7331UL),
        "",
        new byte[] { 7, 3, 3, 1 });

    private static void AssertGearContentEqual(GearInstance expected, GearInstance actual)
    {
        Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
        Assert.Equal(expected.GenerationRuleVersion, actual.GenerationRuleVersion);
        Assert.Equal(expected.BaseCode, actual.BaseCode);
        Assert.Equal(expected.RequiredLevel, actual.RequiredLevel);
        Assert.Equal(expected.RarityCode, actual.RarityCode);
        Assert.Equal(expected.EquipmentRole, actual.EquipmentRole);
        Assert.Equal(expected.VisualChassisAssetCode, actual.VisualChassisAssetCode);
        Assert.Equal(expected.Resolved, actual.Resolved);
        Assert.Equal(expected.Provenance.SourceKind, actual.Provenance.SourceKind);
        Assert.Equal(expected.Provenance.SourceCode, actual.Provenance.SourceCode);
        Assert.Equal(expected.Provenance.Tags, actual.Provenance.Tags);
        Assert.Equal(expected.Provenance.SelectionSeed, actual.Provenance.SelectionSeed);
        Assert.Equal(expected.UniqueCode, actual.UniqueCode);
        Assert.Equal(expected.UniqueRulePayload, actual.UniqueRulePayload);
        Assert.Equal(expected.Affixes.Count, actual.Affixes.Count);
        for (int index = 0; index < expected.Affixes.Count; index++)
        {
            Assert.Equal(expected.Affixes[index], actual.Affixes[index]);
        }
    }

    private sealed class MemoryArchiveStorage : IPlayerItemArchiveStorage
    {
        private readonly Dictionary<string, byte[]> values = new(StringComparer.Ordinal);

        public int WriteCount { get; private set; }
        public bool FailNextWrite { get; set; }
        public Action? BeforeWrite { get; set; }

        public byte[]? Read(string key) => values.TryGetValue(key, out byte[]? value)
            ? (byte[])value.Clone()
            : null;

        public void Write(string key, byte[] data)
        {
            BeforeWrite?.Invoke();
            if (FailNextWrite)
            {
                FailNextWrite = false;
                throw new InvalidOperationException("Simulated interrupted write.");
            }
            values[key] = (byte[])data.Clone();
            WriteCount++;
        }

        public void Corrupt(string key)
        {
            byte[] value = values[key];
            values[key] = value.Take(Math.Max(1, value.Length / 2)).ToArray();
        }
    }
}
