using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using VintageKinematics.Api.Storage;
using VintageKinematics.Storage.Index;
using VintageKinematics.Storage.Persistence;
using Xunit;

namespace VintageKinematics.Tests.Storage.Persistence
{
    public class StoragePersistenceMetadataTests
    {
        [Fact]
        public void InvalidNextEntryId_IsQuarantinedAndReconstructed()
        {
            TestCollectibleResolver resolver = new TestCollectibleResolver();
            ItemStack stack = StorageTestStacks.Create("game:existing");
            resolver.Register(stack);
            KineticStoragePersistence persistence = StoragePersistenceTestContext.CreatePersistence(resolver);
            PersistedStorageEntry record = CreateRecord(5, "game:existing", 1);
            byte[] bytes = persistence.Encode(new StoragePersistenceSnapshot(3, new[] { record }));

            StorageLoadResult loaded = persistence.Load(bytes, new StorageIndexLimits(100));
            loaded.Index.TryInsert(null, StorageTestStacks.Create("game:new"), out _);

            Assert.Contains(
                loaded.QuarantinedEntries,
                entry => entry.Reason == StorageQuarantineReason.InvalidNextEntryId);
            Assert.Equal(6, loaded.Index.GetEntries().Max(entry => entry.EntryId));
        }

        [Fact]
        public void QuantityOverflow_QuarantinesOverflowingRecord()
        {
            TestCollectibleResolver resolver = new TestCollectibleResolver();
            ItemStack first = StorageTestStacks.Create("game:first");
            ItemStack second = StorageTestStacks.Create("game:second");
            resolver.Register(first);
            resolver.Register(second);
            KineticStoragePersistence persistence = StoragePersistenceTestContext.CreatePersistence(resolver);
            StoragePersistenceSnapshot snapshot = new StoragePersistenceSnapshot(
                3,
                new[]
                {
                    CreateRecord(1, "game:first", long.MaxValue),
                    CreateRecord(2, "game:second", 1)
                });

            StorageLoadResult loaded = persistence.Load(
                persistence.Encode(snapshot),
                new StorageIndexLimits(long.MaxValue));

            Assert.Equal(long.MaxValue, loaded.Index.StoredItems);
            Assert.Equal(
                StorageQuarantineReason.QuantityOverflow,
                Assert.Single(loaded.QuarantinedEntries).Reason);
        }

        [Fact]
        public void ValidOverCapacitySnapshot_LoadsInOverCapacityState()
        {
            TestCollectibleResolver resolver = new TestCollectibleResolver();
            ItemStack stack = StorageTestStacks.Create("game:stick");
            resolver.Register(stack);
            KineticStoragePersistence persistence = StoragePersistenceTestContext.CreatePersistence(resolver);
            StoragePersistenceSnapshot snapshot = new StoragePersistenceSnapshot(
                2,
                new[] { CreateRecord(1, "game:stick", 20) });

            StorageLoadResult loaded = persistence.Load(
                persistence.Encode(snapshot),
                new StorageIndexLimits(10));

            Assert.Empty(loaded.QuarantinedEntries);
            Assert.Equal(StorageState.OverCapacity, loaded.SuggestedState);
            Assert.Equal(20, loaded.Index.StoredItems);
        }

        [Fact]
        public void MalformedSnapshot_IsRetainedAsQuarantine()
        {
            KineticStoragePersistence persistence = StoragePersistenceTestContext
                .CreatePersistence(new TestCollectibleResolver());
            byte[] malformed = new byte[] { 1, 2, 3, 4 };

            StorageLoadResult loaded = persistence.Load(malformed, new StorageIndexLimits(100));
            QuarantinedStorageEntry quarantine = Assert.Single(loaded.QuarantinedEntries);

            Assert.Equal(StorageQuarantineReason.MalformedSnapshot, quarantine.Reason);
            Assert.Equal(malformed, quarantine.RawBytes);
            Assert.Equal(StorageState.Corrupt, loaded.SuggestedState);
        }

        [Fact]
        public void ZeroQuantityWithValidChecksum_IsQuarantined()
        {
            byte[] attributes = StorageAttributeCodec.Encode(new TreeAttribute());
            byte[] raw = StorageEntryCodec.EncodeRaw(
                1,
                EnumItemClass.Item,
                "game:stick",
                attributes,
                0);
            PersistedStorageEntry invalid = new PersistedStorageEntry(
                1,
                EnumItemClass.Item,
                "game:stick",
                attributes,
                0,
                raw);
            KineticStoragePersistence persistence = StoragePersistenceTestContext
                .CreatePersistence(new TestCollectibleResolver());

            StorageLoadResult loaded = persistence.Load(
                persistence.Encode(new StoragePersistenceSnapshot(2, new[] { invalid })),
                new StorageIndexLimits(100));

            Assert.Equal(
                StorageQuarantineReason.InvalidQuantity,
                Assert.Single(loaded.QuarantinedEntries).Reason);
        }

        private static PersistedStorageEntry CreateRecord(long id, string code, long quantity)
        {
            return StorageEntryCodec.Create(
                id,
                EnumItemClass.Item,
                code,
                StorageAttributeCodec.Encode(new TreeAttribute()),
                quantity);
        }
    }
}
