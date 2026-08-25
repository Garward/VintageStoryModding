using System;
using System.Collections.Generic;
using System.Linq;
using VintageKinematics.Storage.Recovery;
using Xunit;

namespace VintageKinematics.Tests.Storage.Recovery
{
    public class StorageRecoveryPersistenceTests
    {
        private const string FirstId = "00000000-0000-0000-0000-000000000001";
        private const string SecondId = "00000000-0000-0000-0000-000000000002";
        private static readonly StorageControllerLocation Controller = new(12, 34, -56, 2);

        [Fact]
        public void MissingIndex_LoadsAnEmptyWritableRegistry()
        {
            StorageRecoveryPersistence persistence = new StorageRecoveryPersistence();

            StorageRecoveryLoadResult loaded = persistence.Load(new MemoryRecoveryStore());

            Assert.True(loaded.CanPersist);
            Assert.Empty(loaded.Issues);
            Assert.Empty(loaded.Registry.GetRecords());
        }

        [Fact]
        public void SaveAndLoad_RoundTripsRegistryAndWritesIndexLast()
        {
            StorageRecoveryRegistry registry = new StorageRecoveryRegistry();
            registry.Upsert(CreateRecord(SecondId, 2, 20));
            registry.Upsert(CreateRecord(FirstId, 1, 10));
            MemoryRecoveryStore store = new MemoryRecoveryStore();
            StorageRecoveryPersistence persistence = new StorageRecoveryPersistence();

            bool saved = persistence.Save(store, registry);
            StorageRecoveryLoadResult loaded = persistence.Load(store);

            Assert.True(saved);
            Assert.False(registry.IsDirty);
            Assert.Equal(StorageRecoveryKeys.IndexSlot(1), store.Writes.Last());
            Assert.Equal(2, loaded.Registry.Count);
            Assert.True(loaded.CanPersist);
            Assert.Empty(loaded.Issues);
            Assert.Equal(
                new[] { FirstId, SecondId },
                loaded.Registry.GetRecords().Select(record => record.WarehouseId));
        }

        [Fact]
        public void FailedSave_DoesNotAcknowledgeDirtyRecords()
        {
            StorageRecoveryRegistry registry = new StorageRecoveryRegistry();
            registry.Upsert(CreateRecord(FirstId, 1, 10));
            MemoryRecoveryStore store = new MemoryRecoveryStore
            {
                FailingKey = StorageRecoveryKeys.IndexSlot(1)
            };
            StorageRecoveryPersistence persistence = new StorageRecoveryPersistence();

            Assert.Throws<InvalidOperationException>(() => persistence.Save(store, registry));
            Assert.True(registry.IsDirty);
            Assert.Single(registry.CaptureSaveBatch().DirtyRecords);
        }

        [Fact]
        public void InterruptedUpdate_PreservesThePreviouslyCommittedGeneration()
        {
            StorageRecoveryRegistry registry = new StorageRecoveryRegistry();
            StorageRecoveryRecord original = CreateRecord(FirstId, 1, 10);
            registry.Upsert(original);
            MemoryRecoveryStore store = new MemoryRecoveryStore();
            StorageRecoveryPersistence persistence = new StorageRecoveryPersistence();
            persistence.Save(store, registry);

            StorageRecoveryRecord replacement = CreateRecord(FirstId, 2, 20);
            registry.Upsert(replacement);
            store.FailingKey = StorageRecoveryKeys.IndexSlot(0);

            Assert.Throws<InvalidOperationException>(() => persistence.Save(store, registry));
            StorageRecoveryLoadResult loaded = persistence.Load(store);

            Assert.True(loaded.CanPersist);
            Assert.True(loaded.Registry.TryGet(FirstId, out StorageRecoveryRecord recovered));
            Assert.Equal(original.Revision, recovered.Revision);
            Assert.Equal(original.SnapshotBytes, recovered.SnapshotBytes);
            Assert.True(store.Contains(StorageRecoveryKeys.WarehouseSlot(FirstId, 0)));
            Assert.True(store.Contains(StorageRecoveryKeys.WarehouseSlot(FirstId, 1)));
        }

        [Fact]
        public void TruncatedInactiveIndexSlot_FallsBackToPreviousCommittedSlot()
        {
            StorageRecoveryRegistry registry = new StorageRecoveryRegistry();
            StorageRecoveryRecord original = CreateRecord(FirstId, 1, 10);
            registry.Upsert(original);
            MemoryRecoveryStore store = new MemoryRecoveryStore();
            StorageRecoveryPersistence persistence = new StorageRecoveryPersistence();
            persistence.Save(store, registry);
            registry.Upsert(CreateRecord(FirstId, 2, 20));
            store.FailingKey = StorageRecoveryKeys.IndexSlot(0);

            Assert.Throws<InvalidOperationException>(() => persistence.Save(store, registry));
            StorageRecoveryLoadResult loaded = new StorageRecoveryPersistence().Load(store);

            Assert.True(loaded.CanPersist);
            Assert.True(loaded.Registry.TryGet(FirstId, out StorageRecoveryRecord recovered));
            Assert.Equal(original.Revision, recovered.Revision);
        }

        [Fact]
        public void CorruptNewestIndexSlot_FallsBackToOtherSlotAndRemainsWritable()
        {
            StorageRecoveryRegistry registry = new StorageRecoveryRegistry();
            registry.Upsert(CreateRecord(FirstId, 1, 10));
            MemoryRecoveryStore store = new MemoryRecoveryStore();
            StorageRecoveryPersistence persistence = new StorageRecoveryPersistence();
            persistence.Save(store, registry);
            registry.Upsert(CreateRecord(FirstId, 2, 20));
            persistence.Save(store, registry);
            store.Set(StorageRecoveryKeys.IndexSlot(0), new byte[] { 1, 2, 3 });

            StorageRecoveryPersistence reloadedPersistence = new StorageRecoveryPersistence();
            StorageRecoveryLoadResult loaded = reloadedPersistence.Load(store);
            bool repaired = reloadedPersistence.Save(store, loaded.Registry);

            Assert.True(loaded.CanPersist);
            Assert.True(repaired);
            Assert.True(StorageRecoveryIndexCommitCodec.TryDecode(
                store.Get(StorageRecoveryKeys.IndexSlot(0)),
                out _));
        }

        [Fact]
        public void RepeatedSavesReuseTwoBoundedRecordSlots()
        {
            StorageRecoveryRegistry registry = new StorageRecoveryRegistry();
            MemoryRecoveryStore store = new MemoryRecoveryStore();
            StorageRecoveryPersistence persistence = new StorageRecoveryPersistence();
            for (int revision = 1; revision <= 8; revision++)
            {
                registry.Upsert(CreateRecord(FirstId, revision, (byte)revision));
                persistence.Save(store, registry);
            }

            Assert.True(store.Contains(StorageRecoveryKeys.WarehouseSlot(FirstId, 0)));
            Assert.True(store.Contains(StorageRecoveryKeys.WarehouseSlot(FirstId, 1)));
            Assert.Equal(4, store.KeyCount);
            StorageRecoveryLoadResult loaded = new StorageRecoveryPersistence().Load(store);
            Assert.True(loaded.Registry.TryGet(FirstId, out StorageRecoveryRecord record));
            Assert.Equal(8, record.Revision);
        }

        [Fact]
        public void ControllerAndRecoveryMirrorsCommitIndependently()
        {
            StorageRecoveryRegistry controller = new StorageRecoveryRegistry();
            StorageRecoveryRegistry recovery = new StorageRecoveryRegistry();
            controller.Upsert(CreateRecord(FirstId, 1, 10));
            recovery.Upsert(CreateRecord(FirstId, 1, 20));
            MemoryRecoveryStore store = new MemoryRecoveryStore();
            StorageRecoveryPersistence controllerPersistence = new StorageRecoveryPersistence(
                StorageRecoveryKeyspace.Controller);
            StorageRecoveryPersistence recoveryPersistence = new StorageRecoveryPersistence(
                StorageRecoveryKeyspace.Recovery);

            controllerPersistence.Save(store, controller);
            recoveryPersistence.Save(store, recovery);
            StorageRecoveryLoadResult loadedController = controllerPersistence.Load(store);
            StorageRecoveryLoadResult loadedRecovery = recoveryPersistence.Load(store);

            Assert.True(loadedController.Registry.TryGet(FirstId, out StorageRecoveryRecord first));
            Assert.True(loadedRecovery.Registry.TryGet(FirstId, out StorageRecoveryRecord second));
            Assert.Equal(new byte[] { 10 }, first.SnapshotBytes);
            Assert.Equal(new byte[] { 20 }, second.SnapshotBytes);
        }

        [Fact]
        public void FailureDuringMultiWarehouseRecordWritesPreservesTheOldCommit()
        {
            StorageRecoveryRegistry registry = new StorageRecoveryRegistry();
            registry.Upsert(CreateRecord(FirstId, 1, 10));
            registry.Upsert(CreateRecord(SecondId, 1, 11));
            MemoryRecoveryStore store = new MemoryRecoveryStore();
            StorageRecoveryPersistence persistence = new StorageRecoveryPersistence();
            persistence.Save(store, registry);
            registry.Upsert(CreateRecord(FirstId, 2, 20));
            registry.Upsert(CreateRecord(SecondId, 2, 21));
            store.FailingKey = StorageRecoveryKeys.WarehouseSlot(SecondId, 1);

            Assert.Throws<InvalidOperationException>(() => persistence.Save(store, registry));
            StorageRecoveryLoadResult loaded = new StorageRecoveryPersistence().Load(store);

            Assert.True(loaded.CanPersist);
            Assert.True(loaded.Registry.TryGet(FirstId, out StorageRecoveryRecord first));
            Assert.True(loaded.Registry.TryGet(SecondId, out StorageRecoveryRecord second));
            Assert.Equal(1, first.Revision);
            Assert.Equal(1, second.Revision);
        }

        [Fact]
        public void CrashBetweenMirrorCommitsProducesDetectableDivergence()
        {
            MemoryRecoveryStore store = new MemoryRecoveryStore();
            StorageRecoveryPersistence controllerPersistence = new StorageRecoveryPersistence(
                StorageRecoveryKeyspace.Controller);
            StorageRecoveryPersistence recoveryPersistence = new StorageRecoveryPersistence(
                StorageRecoveryKeyspace.Recovery);
            StorageRecoveryRegistry controller = new StorageRecoveryRegistry();
            StorageRecoveryRegistry recovery = new StorageRecoveryRegistry();
            StorageRecoveryRecord original = CreateRecord(FirstId, 1, 10);
            controller.Upsert(original);
            recovery.Upsert(original);
            controllerPersistence.Save(store, controller);
            recoveryPersistence.Save(store, recovery);
            controller.Upsert(CreateRecord(FirstId, 2, 20));
            controllerPersistence.Save(store, controller);

            StorageRecoveryRecord primary = Assert.Single(
                new StorageRecoveryPersistence(StorageRecoveryKeyspace.Controller)
                    .Load(store).Registry.GetRecords());
            StorageRecoveryRecord mirror = Assert.Single(
                new StorageRecoveryPersistence(StorageRecoveryKeyspace.Recovery)
                    .Load(store).Registry.GetRecords());
            StorageReconciliationResult result = StorageSnapshotReconciler.Reconcile(
                StorageSnapshotCopy.FromRecord(primary),
                StorageSnapshotCopy.FromRecord(mirror));

            Assert.Equal(StorageReconciliationOutcome.Divergent, result.Outcome);
            Assert.True(result.RequiresConfirmation);
            Assert.Equal(StorageSnapshotSource.BlockEntity, result.ProposedSource);
        }

        [Fact]
        public void LegacySingleKeyRecord_RemainsReadable()
        {
            StorageRecoveryRecord record = CreateRecord(FirstId, 1, 10);
            MemoryRecoveryStore store = StoreIndexOnly(record);
            store.Set(
                StorageRecoveryKeys.Warehouse(FirstId),
                StorageRecoveryRegistryCodec.EncodeRecord(record));

            StorageRecoveryLoadResult loaded = new StorageRecoveryPersistence().Load(store);

            Assert.True(loaded.CanPersist);
            Assert.True(loaded.Registry.TryGet(FirstId, out StorageRecoveryRecord recovered));
            Assert.True(record.IsEquivalentTo(recovered));
        }

        [Fact]
        public void MissingReferencedRecord_DisablesPersistenceWithoutDroppingIndexEvidence()
        {
            StorageRecoveryRecord record = CreateRecord(FirstId, 1, 10);
            MemoryRecoveryStore store = StoreIndexOnly(record);

            StorageRecoveryLoadResult loaded = new StorageRecoveryPersistence().Load(store);
            StorageRecoveryLoadIssue issue = Assert.Single(loaded.Issues);

            Assert.False(loaded.CanPersist);
            Assert.Equal(StorageRecoveryLoadIssueKind.RecordMissing, issue.Kind);
            Assert.Equal(FirstId, issue.WarehouseId);
            Assert.NotNull(issue.IndexEntry);
            Assert.Empty(loaded.Registry.GetRecords());
        }

        [Fact]
        public void MalformedRecord_IsRetainedAndDisablesPersistence()
        {
            StorageRecoveryRecord record = CreateRecord(FirstId, 1, 10);
            MemoryRecoveryStore store = StoreIndexOnly(record);
            byte[] malformed = new byte[] { 1, 2, 3 };
            store.Set(StorageRecoveryKeys.Warehouse(FirstId), malformed);

            StorageRecoveryLoadResult loaded = new StorageRecoveryPersistence().Load(store);
            StorageRecoveryLoadIssue issue = Assert.Single(loaded.Issues);

            Assert.False(loaded.CanPersist);
            Assert.Equal(StorageRecoveryLoadIssueKind.RecordMalformed, issue.Kind);
            Assert.Equal(malformed, issue.RawBytes);
            Assert.Empty(loaded.Registry.GetRecords());
        }

        [Fact]
        public void IndexRecordMismatch_RetainsBothCopiesAndDisablesPersistence()
        {
            StorageRecoveryRecord indexed = CreateRecord(FirstId, 1, 10);
            StorageRecoveryRecord stored = CreateRecord(FirstId, 2, 20);
            MemoryRecoveryStore store = StoreIndexOnly(indexed);
            store.Set(
                StorageRecoveryKeys.Warehouse(FirstId),
                StorageRecoveryRegistryCodec.EncodeRecord(stored));

            StorageRecoveryLoadResult loaded = new StorageRecoveryPersistence().Load(store);
            StorageRecoveryLoadIssue issue = Assert.Single(loaded.Issues);

            Assert.False(loaded.CanPersist);
            Assert.Equal(StorageRecoveryLoadIssueKind.RecordIndexMismatch, issue.Kind);
            Assert.True(issue.IndexEntry.Matches(indexed));
            Assert.Equal(stored.Revision, issue.Record.Revision);
            Assert.Empty(loaded.Registry.GetRecords());
        }

        [Fact]
        public void InvalidChecksumRecord_RemainsAvailableButDisablesPersistence()
        {
            StorageRecoveryRecord invalid = StorageRecoveryRecord.Restore(
                FirstId,
                Controller,
                1,
                new byte[] { 10 },
                new byte[32],
                false);
            MemoryRecoveryStore store = StoreIndexOnly(invalid);
            store.Set(
                StorageRecoveryKeys.Warehouse(FirstId),
                StorageRecoveryRegistryCodec.EncodeRecord(invalid));

            StorageRecoveryLoadResult loaded = new StorageRecoveryPersistence().Load(store);
            StorageRecoveryLoadIssue issue = Assert.Single(loaded.Issues);

            Assert.False(loaded.CanPersist);
            Assert.Equal(StorageRecoveryLoadIssueKind.RecordInvalidChecksum, issue.Kind);
            Assert.True(loaded.Registry.TryGet(FirstId, out StorageRecoveryRecord retained));
            Assert.False(retained.HasValidChecksum);
        }

        [Fact]
        public void MalformedIndex_DisablesPersistenceBeforeReadingWarehouseKeys()
        {
            MemoryRecoveryStore store = new MemoryRecoveryStore();
            store.Set(StorageRecoveryKeys.Index, new byte[] { 1, 2, 3 });

            StorageRecoveryLoadResult loaded = new StorageRecoveryPersistence().Load(store);

            Assert.False(loaded.CanPersist);
            Assert.Equal(StorageRecoveryLoadIssueKind.IndexMalformed, Assert.Single(loaded.Issues).Kind);
            Assert.Equal(
                new[]
                {
                    StorageRecoveryKeys.IndexSlot(0),
                    StorageRecoveryKeys.IndexSlot(1),
                    StorageRecoveryKeys.Index
                },
                store.Reads);
        }

        private static MemoryRecoveryStore StoreIndexOnly(StorageRecoveryRecord record)
        {
            MemoryRecoveryStore store = new MemoryRecoveryStore();
            store.Set(
                StorageRecoveryKeys.Index,
                StorageRecoveryRegistryCodec.EncodeIndex(
                    new[] { new StorageRecoveryIndexEntry(record) }));
            return store;
        }

        private static StorageRecoveryRecord CreateRecord(string id, long revision, byte value)
        {
            return StorageRecoveryRecord.Create(id, Controller, revision, new[] { value });
        }

        private sealed class MemoryRecoveryStore : IStorageRecoveryStore
        {
            private readonly Dictionary<string, byte[]> data = new(StringComparer.Ordinal);

            public List<string> Reads { get; } = new List<string>();
            public List<string> Writes { get; } = new List<string>();
            public string FailingKey { get; set; }

            public byte[] Get(string key)
            {
                Reads.Add(key);
                return data.TryGetValue(key, out byte[] value) ? (byte[])value.Clone() : null;
            }

            public void Store(string key, byte[] value)
            {
                Writes.Add(key);
                if (key == FailingKey) throw new InvalidOperationException("Simulated store failure.");
                Set(key, value);
            }

            public void Set(string key, byte[] value)
            {
                data[key] = (byte[])value.Clone();
            }

            public bool Contains(string key)
            {
                return data.ContainsKey(key);
            }

            public int KeyCount => data.Count;
        }
    }
}
