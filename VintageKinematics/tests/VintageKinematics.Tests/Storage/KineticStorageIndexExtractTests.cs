using Vintagestory.API.Common;
using VintageKinematics.Api.Storage;
using VintageKinematics.Storage.Index;
using Xunit;

namespace VintageKinematics.Tests.Storage
{
    public class KineticStorageIndexExtractTests
    {
        [Fact]
        public void TryExtract_ClampsToCollectibleMaxStackSize()
        {
            KineticStorageIndex index = CreateIndex();
            index.TryInsert(null, StorageTestStacks.Create("game:parts", 50, maxStackSize: 16), out _);
            long entryId = Assert.Single(index.GetEntries()).EntryId;

            StorageTransferResult result = index.TryExtract(entryId, 40, out ItemStack extracted);

            Assert.Equal(16, result.Moved);
            Assert.Equal(16, extracted.StackSize);
            Assert.Equal(34, index.StoredItems);
        }

        [Fact]
        public void TryExtract_RemovesEmptyEntry()
        {
            KineticStorageIndex index = CreateIndex();
            index.TryInsert(null, StorageTestStacks.Create("game:stick", 3), out _);
            long entryId = Assert.Single(index.GetEntries()).EntryId;

            index.TryExtract(entryId, 3, out ItemStack extracted);

            Assert.Equal(3, extracted.StackSize);
            Assert.Equal(0, index.EntryCount);
            Assert.Equal(0, index.StoredItems);
        }

        [Fact]
        public void TryExtract_RejectsStaleEntryId()
        {
            KineticStorageIndex index = CreateIndex();

            StorageTransferResult result = index.TryExtract(999, 1, out ItemStack extracted);

            Assert.Equal(StorageTransferStatus.NotFound, result.Status);
            Assert.Null(extracted);
        }

        [Fact]
        public void TryExtract_RejectsNonPositiveQuantityWithoutMutation()
        {
            KineticStorageIndex index = CreateIndex();
            index.TryInsert(null, StorageTestStacks.Create("game:stick", 3), out _);
            long entryId = Assert.Single(index.GetEntries()).EntryId;

            StorageTransferResult result = index.TryExtract(entryId, 0, out ItemStack extracted);

            Assert.Equal(StorageTransferStatus.InvalidQuantity, result.Status);
            Assert.Null(extracted);
            Assert.Equal(3, index.StoredItems);
        }

        [Fact]
        public void RemovedEntryId_IsNeverReused()
        {
            KineticStorageIndex index = CreateIndex();
            index.TryInsert(null, StorageTestStacks.Create("game:stick"), out _);
            long firstId = Assert.Single(index.GetEntries()).EntryId;
            index.TryExtract(firstId, 1, out _);

            index.TryInsert(null, StorageTestStacks.Create("game:stick"), out _);
            long secondId = Assert.Single(index.GetEntries()).EntryId;

            Assert.True(secondId > firstId);
        }

        private static KineticStorageIndex CreateIndex()
        {
            return new KineticStorageIndex(
                new StorageIndexLimits(1000),
                new AcceptAllStorageValidator(),
                VKStorageKeys.KeyFor,
                StorageTestStacks.ExactMatch);
        }
    }
}
