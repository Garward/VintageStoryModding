using System;
using System.Collections.Generic;
using System.Linq;
using VRPG.Data;
using VRPG.Inventory;
using VRPG.Inventory.Gear;
using Xunit;

namespace VRPG.Tests;

public sealed class GearEquipmentServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingMechanicalDefinitionCannotEnterOrRemainInAnEquipmentSnapshot(bool rarity)
    {
        var f = new GearReadinessValidatorTests.Fixture();
        string id = f.Insert(GearItemStateCodec.Encode(GearReadinessValidatorTests.Instance()));
        if (rarity) f.Data.Rarities.Load(Array.Empty<VRPG.Data.Definitions.GearRarityDefinition>());
        else f.Data.Affixes.Load(Array.Empty<VRPG.Data.Definitions.GearAffixDefinition>());
        var equipment = new GearEquipmentService(f.Data);
        Assert.False(equipment.TryEquip(f.Store, 100, "equip", id, PlayerEquipmentRoles.MainWeapon, f.Store.Revision).Success);
        Assert.True(f.Store.Equip("fixture-corruption", id, PlayerEquipmentRoles.MainWeapon, f.Store.Revision).Success);
        Assert.False(equipment.Snapshot(f.Store).Valid);
    }

    [Fact]
    public void DetailLookupReturnsDeepClones()
    {
        IPlayerItemStore store = StoreWithGear(1, PlayerEquipmentRoles.MainWeapon);
        string id = store.Query(GearQuery()).Entries.Single().EntryId;

        Assert.True(store.TryGet(id, out PlayerItemDetail? first));
        first!.Prototype.ExactState[0] ^= 0xff;
        first.Prototype.EmbeddedReferences![0] = new CanonicalCollectibleReference(
            PlayerItemAssetKind.Block,
            99,
            "changed:block");
        Assert.True(store.TryGet(id, out PlayerItemDetail? second));

        Assert.NotEqual(first.Prototype.ExactState[0], second!.Prototype.ExactState[0]);
        Assert.Equal("game:nested", second.Prototype.EmbeddedReferences![0].AssetCode);
        Assert.False(store.TryGet("missing", out PlayerItemDetail? missing));
        Assert.Null(missing);
    }

    [Fact]
    public void EquipRejectsWrongRoleAndUnmetRequirementWithoutMovingEntry()
    {
        IPlayerItemStore store = StoreWithGear(requiredLevel: 20, role: PlayerEquipmentRoles.Head);
        string entryId = store.Query(GearQuery()).Entries.Single().EntryId;
        GearEquipmentService service = GearEquipmentServiceForFixtures(PlayerEquipmentRoles.Head);

        PlayerItemOperationResult wrongRole = service.TryEquip(
            store, playerLevel: 20, "wrong-role", entryId, PlayerEquipmentRoles.Feet, store.Revision);
        PlayerItemOperationResult underLevel = service.TryEquip(
            store, playerLevel: 19, "under-level", entryId, PlayerEquipmentRoles.Head, store.Revision);

        Assert.Equal(PlayerItemOperationRejection.EquipmentRoleMismatch, wrongRole.Rejection);
        Assert.Equal(PlayerItemOperationRejection.RequirementNotMet, underLevel.Rejection);
        Assert.Empty(store.Equipment().Entries);
        Assert.Equal(PlayerItemLocation.Stored, store.Query(GearQuery()).Entries.Single().Location);
    }

    [Fact]
    public void EquipRejectsMissingMalformedAndNonGearEntriesWithoutMovingThem()
    {
        GearEquipmentService service = GearEquipmentServiceForFixtures();
        var malformed = new InMemoryPlayerItemStore(() => "malformed");
        malformed.Insert(new PlayerItemPrototype(
            PlayerItemStorageKind.Unique,
            "game:sword",
            PlayerItemCategories.Gear,
            "Malformed",
            new byte[] { 1, 2, 3 }), 1, 0);
        var wrongCategory = new InMemoryPlayerItemStore(() => "wrong-category");
        wrongCategory.Insert(Prototype(Gear(1, PlayerEquipmentRoles.MainWeapon)) with
        {
            Category = PlayerItemCategories.Materials
        }, 1, 0);
        var wrongStorage = new InMemoryPlayerItemStore(() => "wrong-storage");
        wrongStorage.Insert(Prototype(Gear(1, PlayerEquipmentRoles.MainWeapon)) with
        {
            Kind = PlayerItemStorageKind.Aggregate
        }, 1, 0);

        PlayerItemOperationResult missing = service.TryEquip(
            malformed, 1, "missing", "missing", PlayerEquipmentRoles.MainWeapon, malformed.Revision);
        PlayerItemOperationResult invalidState = service.TryEquip(
            malformed, 1, "malformed", "malformed", PlayerEquipmentRoles.MainWeapon, malformed.Revision);
        PlayerItemOperationResult invalidCategory = service.TryEquip(
            wrongCategory, 1, "category", "wrong-category", PlayerEquipmentRoles.MainWeapon, wrongCategory.Revision);
        PlayerItemOperationResult invalidStorage = service.TryEquip(
            wrongStorage, 1, "storage", "wrong-storage", PlayerEquipmentRoles.MainWeapon, wrongStorage.Revision);

        Assert.Equal(PlayerItemOperationRejection.InvalidGear, missing.Rejection);
        Assert.Equal(PlayerItemOperationRejection.InvalidGear, invalidState.Rejection);
        Assert.Equal(PlayerItemOperationRejection.InvalidGear, invalidCategory.Rejection);
        Assert.Equal(PlayerItemOperationRejection.InvalidGear, invalidStorage.Rejection);
        Assert.Empty(malformed.Equipment().Entries);
        Assert.Empty(wrongCategory.Equipment().Entries);
        Assert.Empty(wrongStorage.Equipment().Entries);
    }

    [Fact]
    public void EquipRejectsRecoveryEntriesAndDefinitionRoleMismatch()
    {
        var recovery = Assert.IsType<InMemoryPlayerItemStore>(
            StoreWithGear(1, PlayerEquipmentRoles.MainWeapon));
        string recoveryId = recovery.Query(GearQuery()).Entries.Single().EntryId;
        PlayerItemOperationResult moved = recovery.MoveToRecovery(
            "recovery",
            recoveryId,
            PlayerItemLocation.Unresolved,
            PlayerItemRecoveryReason.UnsupportedContent,
            "test",
            recovery.Revision);
        IPlayerItemStore mismatched = StoreWithGear(1, PlayerEquipmentRoles.Head);
        string mismatchedId = mismatched.Query(GearQuery()).Entries.Single().EntryId;
        GearEquipmentService service = GearEquipmentServiceForFixtures();

        PlayerItemOperationResult unavailable = service.TryEquip(
            recovery, 1, "equip-recovery", recoveryId, PlayerEquipmentRoles.MainWeapon, moved.Revision);
        PlayerItemOperationResult roleMismatch = service.TryEquip(
            mismatched, 1, "definition-role", mismatchedId, PlayerEquipmentRoles.Head, mismatched.Revision);

        Assert.Equal(PlayerItemOperationRejection.InvalidGear, unavailable.Rejection);
        Assert.Equal(PlayerItemOperationRejection.EquipmentRoleMismatch, roleMismatch.Rejection);
        Assert.Empty(recovery.Equipment().Entries);
        Assert.Empty(mismatched.Equipment().Entries);
    }

    [Fact]
    public void EquipPreservesStaleRevisionAndRequestReplayBehavior()
    {
        IPlayerItemStore store = StoreWithGear(1, PlayerEquipmentRoles.MainWeapon);
        string entryId = store.Query(GearQuery()).Entries.Single().EntryId;
        GearEquipmentService service = GearEquipmentServiceForFixtures();

        PlayerItemOperationResult stale = service.TryEquip(
            store, 1, "stale", entryId, PlayerEquipmentRoles.MainWeapon, store.Revision - 1);
        PlayerItemOperationResult first = service.TryEquip(
            store, 1, "equip", entryId, PlayerEquipmentRoles.MainWeapon, store.Revision);
        PlayerItemOperationResult replay = service.TryEquip(
            store, 1, "equip", entryId, PlayerEquipmentRoles.MainWeapon, first.Revision - 1);

        Assert.Equal(PlayerItemOperationRejection.StaleRevision, stale.Rejection);
        Assert.True(first.Success);
        Assert.Equal(first, replay);
        Assert.Equal(first.Revision, store.Revision);
        Assert.Equal(entryId, store.Equipment().Entries.Single().EntryId);
    }

    [Fact]
    public void SuccessfulEquipReplaysAfterEntryIsUnequippedAndExtracted()
    {
        IPlayerItemStore store = StoreWithGear(1, PlayerEquipmentRoles.MainWeapon);
        string entryId = store.Query(GearQuery()).Entries.Single().EntryId;
        long equipRevision = store.Revision;
        GearEquipmentService service = GearEquipmentServiceForFixtures();
        PlayerItemOperationResult accepted = service.TryEquip(
            store, 1, "equip-then-remove", entryId, PlayerEquipmentRoles.MainWeapon, equipRevision);
        PlayerItemOperationResult unequipped = store.Unequip(
            "unequip-after", PlayerEquipmentRoles.MainWeapon, accepted.Revision);
        PlayerItemMutationResult extracted = store.Extract(
            "extract-after", entryId, 1, unequipped.Revision);

        PlayerItemOperationResult replay = service.TryEquip(
            store, 1, "equip-then-remove", entryId, PlayerEquipmentRoles.MainWeapon, equipRevision);

        Assert.True(extracted.Success);
        Assert.Equal(accepted, replay);
        Assert.Equal(extracted.Revision, store.Revision);
    }

    [Fact]
    public void CompletedEquipRequestConflictsBeforeCurrentGearValidation()
    {
        IPlayerItemStore store = StoreWithGear(1, PlayerEquipmentRoles.MainWeapon);
        string entryId = store.Query(GearQuery()).Entries.Single().EntryId;
        long equipRevision = store.Revision;
        GearEquipmentService service = GearEquipmentServiceForFixtures();
        PlayerItemOperationResult accepted = service.TryEquip(
            store, 1, "completed-equip", entryId, PlayerEquipmentRoles.MainWeapon, equipRevision);
        PlayerItemOperationResult moved = store.MoveToRecovery(
            "move-after-equip",
            entryId,
            PlayerItemLocation.Unresolved,
            PlayerItemRecoveryReason.UnsupportedContent,
            "test",
            accepted.Revision);

        PlayerItemOperationResult differentRole = service.TryEquip(
            store, 1, "completed-equip", entryId, PlayerEquipmentRoles.Head, equipRevision);
        PlayerItemOperationResult differentEntry = service.TryEquip(
            store, 1, "completed-equip", "missing", PlayerEquipmentRoles.MainWeapon, equipRevision);
        PlayerItemOperationResult differentRevision = service.TryEquip(
            store, 1, "completed-equip", entryId, PlayerEquipmentRoles.MainWeapon, moved.Revision);

        Assert.Equal(PlayerItemOperationRejection.RequestIdConflict, differentRole.Rejection);
        Assert.Equal(PlayerItemOperationRejection.RequestIdConflict, differentEntry.Rejection);
        Assert.Equal(PlayerItemOperationRejection.RequestIdConflict, differentRevision.Rejection);
        Assert.Equal(moved.Revision, store.Revision);
    }

    [Fact]
    public void NewRequestStillRequiresCurrentGearValidation()
    {
        IPlayerItemStore store = StoreWithGear(1, PlayerEquipmentRoles.MainWeapon);
        string entryId = store.Query(GearQuery()).Entries.Single().EntryId;
        GearEquipmentService service = GearEquipmentServiceForFixtures();
        PlayerItemOperationResult moved = store.MoveToRecovery(
            "move-new-request",
            entryId,
            PlayerItemLocation.Unresolved,
            PlayerItemRecoveryReason.UnsupportedContent,
            "test",
            store.Revision);

        PlayerItemOperationResult result = service.TryEquip(
            store, 1, "genuinely-new", entryId, PlayerEquipmentRoles.MainWeapon, moved.Revision);

        Assert.Equal(PlayerItemOperationRejection.InvalidGear, result.Rejection);
        Assert.Empty(store.Equipment().Entries);
        Assert.Equal(moved.Revision, store.Revision);
    }

    [Fact]
    public void SnapshotReturnsExactCopySafeEquippedEntries()
    {
        IPlayerItemStore store = StoreWithGear(20, PlayerEquipmentRoles.MainWeapon);
        string entryId = store.Query(GearQuery()).Entries.Single().EntryId;
        GearEquipmentService service = GearEquipmentServiceForFixtures();
        PlayerItemOperationResult equipped = service.TryEquip(
            store, 20, "equip", entryId, PlayerEquipmentRoles.MainWeapon, store.Revision);

        GearEquipmentSnapshot snapshot = service.Snapshot(store);
        GearEquipmentEntry[] exposed = snapshot.Entries;
        exposed[0] = new GearEquipmentEntry(PlayerEquipmentRoles.Head, "changed", Gear(1, PlayerEquipmentRoles.Head));

        Assert.True(snapshot.Valid, snapshot.Error);
        Assert.Equal(equipped.Revision, snapshot.Revision);
        Assert.Equal(entryId, snapshot.Entries.Single().EntryId);
        Assert.Equal(PlayerEquipmentRoles.MainWeapon, snapshot.Entries.Single().RoleCode);
        Assert.Equal(20, snapshot.Entries.Single().Instance.RequiredLevel);
    }

    [Fact]
    public void SnapshotFailsClosedWithOffendingEntryForCorruptOrMismatchedEquipment()
    {
        GearEquipmentService service = GearEquipmentServiceForFixtures();
        var corrupt = new InMemoryPlayerItemStore(() => "corrupt");
        long corruptRevision = corrupt.Insert(new PlayerItemPrototype(
            PlayerItemStorageKind.Unique,
            "game:sword",
            PlayerItemCategories.Gear,
            "Corrupt",
            new byte[] { 9 }), 1, 0).Revision;
        corrupt.Equip("equip-corrupt", "corrupt", PlayerEquipmentRoles.MainWeapon, corruptRevision);
        var mismatched = Assert.IsType<InMemoryPlayerItemStore>(
            StoreWithGear(1, PlayerEquipmentRoles.MainWeapon));
        string mismatchId = mismatched.Query(GearQuery()).Entries.Single().EntryId;
        mismatched.Equip("equip-mismatch", mismatchId, PlayerEquipmentRoles.Head, mismatched.Revision);

        GearEquipmentSnapshot corruptSnapshot = service.Snapshot(corrupt);
        GearEquipmentSnapshot mismatchSnapshot = service.Snapshot(mismatched);

        Assert.False(corruptSnapshot.Valid);
        Assert.Empty(corruptSnapshot.Entries);
        Assert.Contains("corrupt", corruptSnapshot.Error, StringComparison.Ordinal);
        Assert.False(mismatchSnapshot.Valid);
        Assert.Empty(mismatchSnapshot.Entries);
        Assert.Contains(mismatchId, mismatchSnapshot.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotFailsClosedForMissingDuplicateAndRacingEquipment()
    {
        GearEquipmentService service = GearEquipmentServiceForFixtures();
        PlayerItemDetail detail = Detail("gear", Gear(1, PlayerEquipmentRoles.MainWeapon), PlayerItemLocation.Equipped);
        var missing = new SnapshotStore(
            4,
            new[] { new EquippedPlayerItemRecord(PlayerEquipmentRoles.MainWeapon, "missing") },
            Array.Empty<PlayerItemDetail>());
        var duplicateEntry = new SnapshotStore(
            5,
            new[]
            {
                new EquippedPlayerItemRecord(PlayerEquipmentRoles.MainWeapon, "gear"),
                new EquippedPlayerItemRecord(PlayerEquipmentRoles.Head, "gear")
            },
            new[] { detail });
        PlayerItemDetail secondDetail = Detail(
            "gear-b",
            Gear(1, PlayerEquipmentRoles.MainWeapon),
            PlayerItemLocation.Equipped);
        var duplicateRole = new SnapshotStore(
            5,
            new[]
            {
                new EquippedPlayerItemRecord(PlayerEquipmentRoles.MainWeapon, "gear"),
                new EquippedPlayerItemRecord(PlayerEquipmentRoles.MainWeapon, "gear-b")
            },
            new[] { detail, secondDetail });
        var racing = new SnapshotStore(
            6,
            new[] { new EquippedPlayerItemRecord(PlayerEquipmentRoles.MainWeapon, "gear") },
            new[] { detail },
            mutateRevisionOnGet: true);

        GearEquipmentSnapshot missingSnapshot = service.Snapshot(missing);
        GearEquipmentSnapshot duplicateEntrySnapshot = service.Snapshot(duplicateEntry);
        GearEquipmentSnapshot duplicateRoleSnapshot = service.Snapshot(duplicateRole);
        GearEquipmentSnapshot racingSnapshot = service.Snapshot(racing);

        Assert.False(missingSnapshot.Valid);
        Assert.Contains("missing", missingSnapshot.Error, StringComparison.Ordinal);
        Assert.False(duplicateEntrySnapshot.Valid);
        Assert.Contains("gear", duplicateEntrySnapshot.Error, StringComparison.Ordinal);
        Assert.False(duplicateRoleSnapshot.Valid);
        Assert.Contains("gear-b", duplicateRoleSnapshot.Error, StringComparison.Ordinal);
        Assert.False(racingSnapshot.Valid);
        Assert.Empty(racingSnapshot.Entries);
        Assert.Contains("revision", racingSnapshot.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static IPlayerItemStore StoreWithGear(int requiredLevel, string role)
    {
        var store = new InMemoryPlayerItemStore(() => "gear");
        PlayerItemPrototype prototype = Prototype(Gear(requiredLevel, role)) with
        {
            EmbeddedReferences = new[]
            {
                new CanonicalCollectibleReference(PlayerItemAssetKind.Item, 1, "game:nested")
            }
        };
        Assert.True(store.Insert("insert", prototype, 1, 0).Success);
        return store;
    }

    private static PlayerItemQuery GearQuery() => new(
        PlayerItemCategories.Gear,
        "",
        0,
        100,
        PlayerItemLocation.Stored);

    private static GearEquipmentService GearEquipmentServiceForFixtures(
        string role = PlayerEquipmentRoles.MainWeapon)
    {
        VRPGDataRegistry registry = TestGearDefinitions.Registry(data =>
            data.GearBases.Get("vrpg:test-sword")!.EquipmentRole = role);
        return new GearEquipmentService(registry);
    }

    private static GearInstance Gear(int requiredLevel, string role) => new(
        GearInstanceSchema.CurrentVersion,
        GearInstanceSchema.CurrentGenerationRules,
        "vrpg:test-sword",
        requiredLevel,
        "vrpg:common",
        role,
        "game:test-sword",
        Array.Empty<GearAffixRoll>(),
        new GearResolvedStats(10, 2, 10, 5, 0.1f, 0.1f, 0.2f, 0.3f, 0.4f),
        new GearProvenance("test", "equipment", new[] { "fixture" }, 42UL),
        "",
        Array.Empty<byte>());

    private static PlayerItemPrototype Prototype(GearInstance instance) => new(
        PlayerItemStorageKind.Unique,
        "game:test-sword",
        PlayerItemCategories.Gear,
        "Test Sword",
        GearItemStateCodec.Encode(instance));

    private static PlayerItemDetail Detail(string entryId, GearInstance instance, PlayerItemLocation location) => new(
        entryId,
        Prototype(instance),
        1,
        location,
        PlayerItemFlags.None);

    private sealed class SnapshotStore : IPlayerItemStore
    {
        private readonly Dictionary<string, PlayerItemDetail> details;
        private readonly EquippedPlayerItemRecord[] equipment;
        private readonly bool mutateRevisionOnGet;

        public SnapshotStore(
            long revision,
            EquippedPlayerItemRecord[] equipment,
            PlayerItemDetail[] details,
            bool mutateRevisionOnGet = false)
        {
            Revision = revision;
            this.equipment = equipment;
            this.details = details.ToDictionary(detail => detail.EntryId, StringComparer.OrdinalIgnoreCase);
            this.mutateRevisionOnGet = mutateRevisionOnGet;
        }

        public long Revision { get; private set; }

        public PlayerItemCapacitySummary CapacitySummary() => throw new NotSupportedException();

        public bool TryGet(string entryId, out PlayerItemDetail? detail)
        {
            if (mutateRevisionOnGet) Revision++;
            if (!details.TryGetValue(entryId, out PlayerItemDetail? found))
            {
                detail = null;
                return false;
            }
            detail = found with
            {
                Prototype = found.Prototype with
                {
                    ExactState = (byte[])found.Prototype.ExactState.Clone(),
                    EmbeddedReferences = found.Prototype.EmbeddedReferences == null
                        ? null
                        : (CanonicalCollectibleReference[])found.Prototype.EmbeddedReferences.Clone()
                }
            };
            return true;
        }

        public bool TryGetCompletedEquip(
            string requestId,
            string entryId,
            string roleCode,
            long expectedRevision,
            out PlayerItemOperationResult? result)
        {
            result = null;
            return false;
        }

        public PlayerEquipmentSnapshot Equipment() => new(Revision, (EquippedPlayerItemRecord[])equipment.Clone());
        public PlayerItemPage Query(PlayerItemQuery query) => throw new NotSupportedException();
        public PlayerItemCategoryPage QueryCategories(PlayerItemLocation location, int offset, int limit) => throw new NotSupportedException();
        public PlayerItemMutationResult Insert(string requestId, PlayerItemPrototype prototype, long quantity, long expectedRevision) => throw new NotSupportedException();
        public PlayerItemMutationResult Extract(string requestId, string entryId, long quantity, long expectedRevision) => throw new NotSupportedException();
        public PlayerItemReservationResult Reserve(string requestId, PlayerItemAmount[] amounts, long expectedRevision) => throw new NotSupportedException();
        public PlayerItemReservationSnapshot Reservation(string reservationId) => throw new NotSupportedException();
        public PlayerItemOperationResult CommitReservation(string requestId, string reservationId) => throw new NotSupportedException();
        public PlayerItemOperationResult RollbackReservation(string requestId, string reservationId) => throw new NotSupportedException();
        public PlayerItemOperationResult Equip(string requestId, string entryId, string roleCode, long expectedRevision) => throw new NotSupportedException();
        public PlayerItemOperationResult Unequip(string requestId, string roleCode, long expectedRevision) => throw new NotSupportedException();
        public PlayerItemOperationResult SetFlags(string requestId, string entryId, PlayerItemFlags flags, long expectedRevision) => throw new NotSupportedException();
        public PlayerItemOperationResult MoveToRecovery(string requestId, string entryId, PlayerItemLocation destination, PlayerItemRecoveryReason reason, string source, long expectedRevision) => throw new NotSupportedException();
        public PlayerItemMutationResult ImportRecovery(string requestId, PlayerItemPrototype prototype, long quantity, PlayerItemLocation destination, PlayerItemRecoveryReason reason, string source, long expectedRevision) => throw new NotSupportedException();
        public PlayerItemOperationResult Recover(string requestId, string entryId, long expectedRevision) => throw new NotSupportedException();
        public PlayerItemOperationResult Destroy(string requestId, string entryId, long quantity, bool confirmed, long expectedRevision) => throw new NotSupportedException();
        public PlayerRecoverySnapshot Recovery() => throw new NotSupportedException();
    }
}
