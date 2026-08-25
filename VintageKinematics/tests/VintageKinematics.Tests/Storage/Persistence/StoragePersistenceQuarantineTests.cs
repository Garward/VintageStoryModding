using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using VintageKinematics.Api.Storage;
using VintageKinematics.Storage.Index;
using VintageKinematics.Storage.Persistence;
using Xunit;

namespace VintageKinematics.Tests.Storage.Persistence
{
    public class StoragePersistenceQuarantineTests
    {
        [Fact]
        public void ChecksumFailure_QuarantinesExactRawRecord()
        {
            TestCollectibleResolver resolver = new TestCollectibleResolver();
            KineticStoragePersistence persistence = StoragePersistenceTestContext.CreatePersistence(resolver);
            PersistedStorageEntry valid = CreateRecord("game:stick", 1);
            byte[] tampered = valid.RawRecordBytes;
            tampered[0] ^= 0x01;
            PersistedStorageEntry damaged = new PersistedStorageEntry(
                valid.EntryId,
                valid.ItemClass,
                valid.Code,
                valid.AttributeBytes,
                valid.Quantity,
                tampered);
            StoragePersistenceSnapshot snapshot = new StoragePersistenceSnapshot(2, new[] { damaged });

            StorageLoadResult loaded = persistence.Load(
                persistence.Encode(snapshot),
                new StorageIndexLimits(100));
            QuarantinedStorageEntry quarantine = Assert.Single(loaded.QuarantinedEntries);

            Assert.Equal(StorageState.Corrupt, loaded.SuggestedState);
            Assert.Equal(StorageQuarantineReason.ChecksumMismatch, quarantine.Reason);
            Assert.Equal(tampered, quarantine.RawBytes);
            Assert.Equal(0, loaded.Index.StoredItems);
        }

        [Fact]
        public void DuplicateEntryId_IsQuarantinedWithoutMerging()
        {
            TestCollectibleResolver resolver = new TestCollectibleResolver();
            ItemStack stack = StorageTestStacks.Create("game:stick");
            resolver.Register(stack);
            KineticStoragePersistence persistence = StoragePersistenceTestContext.CreatePersistence(resolver);
            PersistedStorageEntry first = CreateRecord("game:stick", 2);
            PersistedStorageEntry duplicate = CreateRecord("game:stick", 3);
            StoragePersistenceSnapshot snapshot = new StoragePersistenceSnapshot(
                2,
                new[] { first, duplicate });

            StorageLoadResult loaded = persistence.Load(
                persistence.Encode(snapshot),
                new StorageIndexLimits(100));

            Assert.Equal(2, loaded.Index.StoredItems);
            Assert.Equal(StorageState.Corrupt, loaded.SuggestedState);
            Assert.Equal(
                StorageQuarantineReason.DuplicateEntryId,
                Assert.Single(loaded.QuarantinedEntries).Reason);
        }

        [Fact]
        public void InvalidAttributePayload_IsQuarantined()
        {
            TestCollectibleResolver resolver = new TestCollectibleResolver();
            ItemStack stack = StorageTestStacks.Create("game:stick");
            resolver.Register(stack);
            KineticStoragePersistence persistence = StoragePersistenceTestContext.CreatePersistence(resolver);
            PersistedStorageEntry invalid = StorageEntryCodec.Create(
                1,
                EnumItemClass.Item,
                "game:stick",
                new byte[] { 255, 0 },
                1);

            StorageLoadResult loaded = persistence.Load(
                persistence.Encode(new StoragePersistenceSnapshot(2, new[] { invalid })),
                new StorageIndexLimits(100));

            Assert.Equal(
                StorageQuarantineReason.InvalidAttributes,
                Assert.Single(loaded.QuarantinedEntries).Reason);
        }

        [Fact]
        public void UnsafeNestedStack_IsQuarantined()
        {
            TestCollectibleResolver resolver = new TestCollectibleResolver();
            ItemStack container = StorageTestStacks.Create("game:container");
            resolver.Register(container);
            TreeAttribute attributes = new TreeAttribute();
            attributes["content"] = new ItemstackAttribute(StorageTestStacks.Create("game:stick"));
            PersistedStorageEntry unsafeRecord = StorageEntryCodec.Create(
                1,
                EnumItemClass.Item,
                "game:container",
                StorageAttributeCodec.Encode(attributes),
                1);
            KineticStoragePersistence persistence = StoragePersistenceTestContext.CreatePersistence(resolver);

            StorageLoadResult loaded = persistence.Load(
                persistence.Encode(new StoragePersistenceSnapshot(2, new[] { unsafeRecord })),
                new StorageIndexLimits(100));

            Assert.Equal(
                StorageQuarantineReason.UnsafeItemState,
                Assert.Single(loaded.QuarantinedEntries).Reason);
        }

        private static PersistedStorageEntry CreateRecord(string code, long quantity)
        {
            return StorageEntryCodec.Create(
                1,
                EnumItemClass.Item,
                code,
                StorageAttributeCodec.Encode(new TreeAttribute()),
                quantity);
        }
    }
}
