using System.Linq;
using Vintagestory.API.Common;
using VintageKinematics.Api.Storage;
using VintageKinematics.Storage.Index;
using VintageKinematics.Storage.Persistence;
using Xunit;

namespace VintageKinematics.Tests.Storage.Persistence
{
    public class StoragePersistenceRoundTripTests
    {
        [Fact]
        public void SaveAndLoad_PreservesIdsQuantitiesAndAttributes()
        {
            TestCollectibleResolver resolver = new TestCollectibleResolver();
            KineticStoragePersistence persistence = StoragePersistenceTestContext.CreatePersistence(resolver);
            KineticStorageIndex original = StoragePersistenceTestContext.CreateIndex();
            ItemStack tool = StorageTestStacks.Create("game:tool", 7);
            tool.Attributes.SetInt("durability", 42);
            resolver.Register(tool);
            original.TryInsert(null, tool, out _);
            long originalId = Assert.Single(original.GetEntries()).EntryId;

            byte[] bytes = persistence.Encode(persistence.Capture(original));
            StorageLoadResult loaded = persistence.Load(bytes, new StorageIndexLimits(1000));
            StoredEntry restored = Assert.Single(loaded.Index.GetEntries());

            Assert.Equal(StorageState.Online, loaded.SuggestedState);
            Assert.Equal(originalId, restored.EntryId);
            Assert.Equal(7, restored.Quantity);
            Assert.Equal(42, restored.Exemplar.Attributes.GetInt("durability"));
        }

        [Fact]
        public void SaveAndLoad_PreservesNextEntryIdAfterDeletion()
        {
            TestCollectibleResolver resolver = new TestCollectibleResolver();
            KineticStoragePersistence persistence = StoragePersistenceTestContext.CreatePersistence(resolver);
            KineticStorageIndex original = StoragePersistenceTestContext.CreateIndex();
            ItemStack first = StorageTestStacks.Create("game:first");
            ItemStack second = StorageTestStacks.Create("game:second");
            resolver.Register(first);
            resolver.Register(second);
            original.TryInsert(null, first, out _);
            long removedId = Assert.Single(original.GetEntries()).EntryId;
            original.TryExtract(removedId, 1, out _);
            original.TryInsert(null, second, out _);

            byte[] bytes = persistence.Encode(persistence.Capture(original));
            StorageLoadResult loaded = persistence.Load(bytes, new StorageIndexLimits(1000));
            loaded.Index.TryInsert(null, StorageTestStacks.Create("game:third"), out _);
            long newestId = loaded.Index.GetEntries().Max(entry => entry.EntryId);

            Assert.Equal(3, newestId);
        }

        [Fact]
        public void MissingCollectible_RetainsRawRecordAndReservesCapacity()
        {
            TestCollectibleResolver savingResolver = new TestCollectibleResolver();
            KineticStoragePersistence saving = StoragePersistenceTestContext.CreatePersistence(savingResolver);
            KineticStorageIndex original = StoragePersistenceTestContext.CreateIndex(capacity: 10);
            ItemStack missingLater = StorageTestStacks.Create("gone:item", 10);
            savingResolver.Register(missingLater);
            original.TryInsert(null, missingLater, out _);
            StoragePersistenceSnapshot snapshot = saving.Capture(original);
            byte[] originalRaw = Assert.Single(snapshot.Entries).RawRecordBytes;

            KineticStoragePersistence loading = StoragePersistenceTestContext.CreatePersistence(new TestCollectibleResolver());
            StorageLoadResult loaded = loading.Load(saving.Encode(snapshot), new StorageIndexLimits(10));
            StorageTransferResult insert = loaded.Index.TryInsert(
                null,
                StorageTestStacks.Create("game:stick"),
                out _);
            StoragePersistenceSnapshot resaved = loading.Capture(loaded.Index, loaded.UnresolvedEntries);

            Assert.Equal(10, loaded.Index.UnresolvedItems);
            Assert.Equal(1, loaded.Index.UnresolvedEntryCount);
            Assert.Equal(StorageTransferStatus.Full, insert.Status);
            Assert.Equal(originalRaw, Assert.Single(resaved.Entries).RawRecordBytes);
        }

        [Fact]
        public void MissingCollectible_ResolvesOnLaterLoad()
        {
            TestCollectibleResolver savingResolver = new TestCollectibleResolver();
            KineticStoragePersistence persistence = StoragePersistenceTestContext.CreatePersistence(savingResolver);
            KineticStorageIndex original = StoragePersistenceTestContext.CreateIndex();
            ItemStack stack = StorageTestStacks.Create("returning:item", 5);
            savingResolver.Register(stack);
            original.TryInsert(null, stack, out _);
            byte[] bytes = persistence.Encode(persistence.Capture(original));

            TestCollectibleResolver restoredResolver = new TestCollectibleResolver();
            restoredResolver.Register(stack);
            StorageLoadResult loaded = StoragePersistenceTestContext
                .CreatePersistence(restoredResolver)
                .Load(bytes, new StorageIndexLimits(1000));

            Assert.Empty(loaded.UnresolvedEntries);
            Assert.Equal(5, Assert.Single(loaded.Index.GetEntries()).Quantity);
        }
    }
}
