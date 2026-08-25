using System.Linq;
using Vintagestory.API.Common;
using VintageKinematics.Api.Storage;
using VintageKinematics.Storage.Index;
using Xunit;

namespace VintageKinematics.Tests.Storage
{
    public class KineticStorageIndexInsertTests
    {
        [Fact]
        public void TryInsert_AggregatesExactlyMatchingStacks()
        {
            KineticStorageIndex index = CreateIndex(itemCapacity: 100);

            index.TryInsert(null, StorageTestStacks.Create("game:stick", 4), out _);
            StorageTransferResult result = index.TryInsert(
                null,
                StorageTestStacks.Create("game:stick", 6),
                out ItemStack remainder);

            Assert.True(result.Success);
            Assert.Equal(6, result.Moved);
            Assert.Null(remainder);
            Assert.Equal(10, index.StoredItems);
            Assert.Equal(1, index.EntryCount);
            Assert.Equal(10, Assert.Single(index.GetEntries()).Quantity);
        }

        [Fact]
        public void TryInsert_KeepsDifferentAttributesInSeparateEntries()
        {
            KineticStorageIndex index = CreateIndex(itemCapacity: 100);
            ItemStack first = StorageTestStacks.Create("game:tool");
            ItemStack second = StorageTestStacks.Create("game:tool");
            first.Attributes.SetInt("durability", 10);
            second.Attributes.SetInt("durability", 9);

            index.TryInsert(null, first, out _);
            index.TryInsert(null, second, out _);

            Assert.Equal(2, index.EntryCount);
        }

        [Fact]
        public void TryInsert_SeparatesForcedHashCollision()
        {
            ItemKey collisionKey = new ItemKey(EnumItemClass.Item, "forced", 0, 1234);
            KineticStorageIndex index = new KineticStorageIndex(
                new StorageIndexLimits(100),
                new AcceptAllStorageValidator(),
                _ => collisionKey,
                StorageTestStacks.ExactMatch);

            index.TryInsert(null, StorageTestStacks.Create("game:copper"), out _);
            index.TryInsert(null, StorageTestStacks.Create("game:tin"), out _);

            Assert.Equal(2, index.EntryCount);
        }

        [Fact]
        public void TryInsert_PartialMovePreservesInputAndReturnsCloneRemainder()
        {
            KineticStorageIndex index = CreateIndex(itemCapacity: 6);
            ItemStack input = StorageTestStacks.Create("game:stick", 10);

            StorageTransferResult result = index.TryInsert(null, input, out ItemStack remainder);

            Assert.Equal(6, result.Moved);
            Assert.Equal(10, input.StackSize);
            Assert.Equal(4, remainder.StackSize);
            Assert.NotSame(input, remainder);
        }

        [Fact]
        public void TryInsert_MaxQuantityLeavesUnconsideredItemsInRemainder()
        {
            KineticStorageIndex index = CreateIndex(itemCapacity: 100);
            ItemStack input = StorageTestStacks.Create("game:stick", 10);

            StorageTransferResult result = index.TryInsert(null, input, out ItemStack remainder, maxQuantity: 4);

            Assert.Equal(4, result.Moved);
            Assert.Equal(6, remainder.StackSize);
            Assert.Equal(10, input.StackSize);
        }

        [Fact]
        public void TryInsert_TypeLimitAppliesOnlyToNewExactEntry()
        {
            KineticStorageIndex index = CreateIndex(itemCapacity: 100, typeCapacity: 1);
            index.TryInsert(null, StorageTestStacks.Create("game:stick"), out _);

            StorageTransferResult aggregate = index.TryInsert(
                null,
                StorageTestStacks.Create("game:stick"),
                out _);
            StorageTransferResult newType = index.TryInsert(
                null,
                StorageTestStacks.Create("game:stone"),
                out _);

            Assert.True(aggregate.Success);
            Assert.Equal(StorageTransferStatus.TypeLimitReached, newType.Status);
        }

        [Fact]
        public void TryInsert_HardEntryLimitRejectsNewEntry()
        {
            KineticStorageIndex index = new KineticStorageIndex(
                new StorageIndexLimits(itemCapacity: 100, maxEntries: 1),
                new AcceptAllStorageValidator(),
                VKStorageKeys.KeyFor,
                StorageTestStacks.ExactMatch);
            index.TryInsert(null, StorageTestStacks.Create("game:stick"), out _);

            StorageTransferResult result = index.TryInsert(
                null,
                StorageTestStacks.Create("game:stone"),
                out _);

            Assert.Equal(StorageTransferStatus.TypeLimitReached, result.Status);
        }

        [Fact]
        public void TryInsert_RejectionPreservesInputAndReturnsPolicyMessage()
        {
            KineticStorageIndex index = new KineticStorageIndex(
                world: null,
                limits: new StorageIndexLimits(100));
            ItemStack input = StorageTestStacks.Create("game:hot-tool", 2);
            input.Attributes.SetFloat("temperature", 100f);

            StorageTransferResult result = index.TryInsert(null, input, out ItemStack remainder);

            Assert.Equal(StorageTransferStatus.ItemRejected, result.Status);
            Assert.Equal("vintagekinematics:storage-reject-temperature", result.MessageLangCode);
            Assert.Equal(2, input.StackSize);
            Assert.Equal(2, remainder.StackSize);
            Assert.NotSame(input, remainder);
            Assert.Equal(0, index.StoredItems);
        }

        [Fact]
        public void TryInsert_RejectsNonPositiveMaximumQuantity()
        {
            KineticStorageIndex index = CreateIndex(itemCapacity: 100);

            StorageTransferResult result = index.TryInsert(
                null,
                StorageTestStacks.Create("game:stick"),
                out _,
                maxQuantity: 0);

            Assert.Equal(StorageTransferStatus.InvalidQuantity, result.Status);
            Assert.Equal(0, index.StoredItems);
        }

        [Fact]
        public void GetEntries_ReturnsClonedSnapshots()
        {
            KineticStorageIndex index = CreateIndex(itemCapacity: 100);
            ItemStack input = StorageTestStacks.Create("game:tool");
            input.Attributes.SetInt("durability", 10);
            index.TryInsert(null, input, out _);

            StoredEntry firstSnapshot = Assert.Single(index.GetEntries());
            firstSnapshot.Exemplar.Attributes.SetInt("durability", 1);
            StoredEntry secondSnapshot = Assert.Single(index.GetEntries());

            Assert.Equal(10, secondSnapshot.Exemplar.Attributes.GetInt("durability"));
        }

        [Fact]
        public void TryFindNextEntry_WrapsByStableIdForFairOutputs()
        {
            KineticStorageIndex index = CreateIndex(itemCapacity: 100);
            index.TryInsert(null, StorageTestStacks.Create("game:stick"), out _);
            index.TryInsert(null, StorageTestStacks.Create("game:stone"), out _);
            StoredEntry[] entries = index.GetEntries().ToArray();

            Assert.True(index.TryFindNextEntry(
                entries[0].EntryId,
                _ => true,
                out StoredEntry second));
            Assert.Equal(entries[1].EntryId, second.EntryId);

            Assert.True(index.TryFindNextEntry(
                entries[1].EntryId,
                _ => true,
                out StoredEntry wrapped));
            Assert.Equal(entries[0].EntryId, wrapped.EntryId);
        }

        private static KineticStorageIndex CreateIndex(long itemCapacity, int typeCapacity = 0)
        {
            return new KineticStorageIndex(
                new StorageIndexLimits(itemCapacity, typeCapacity),
                new AcceptAllStorageValidator(),
                VKStorageKeys.KeyFor,
                StorageTestStacks.ExactMatch);
        }
    }
}
