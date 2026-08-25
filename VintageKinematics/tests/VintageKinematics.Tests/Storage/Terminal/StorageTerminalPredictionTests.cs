using System.Linq;
using Vintagestory.API.Common;
using VintageKinematics.Api.Storage;
using VintageKinematics.Storage.Terminal;
using Xunit;

namespace VintageKinematics.Tests.Storage.Terminal
{
    public sealed class StorageTerminalPredictionTests
    {
        [Fact]
        public void Withdraw_PredictsOneLegalStackWithoutChangingConfirmedPage()
        {
            StorageTerminalPage confirmed = Page(quantity: 40, maxStackSize: 16);

            StorageTerminalPage predicted = StorageTerminalPrediction.Withdraw(confirmed, 7, 16);

            Assert.Equal(24, Assert.Single(predicted.Entries).Quantity);
            Assert.Equal(24, predicted.Stats.StoredItems);
            Assert.Equal(40, Assert.Single(confirmed.Entries).Quantity);
            Assert.Equal(40, confirmed.Stats.StoredItems);
            Assert.Equal(confirmed.Revision, predicted.Revision);
        }

        [Fact]
        public void Withdraw_PredictsEntryRemovalAndMatchingCount()
        {
            StorageTerminalPage confirmed = Page(quantity: 3, maxStackSize: 64);

            StorageTerminalPage predicted = StorageTerminalPrediction.Withdraw(confirmed, 7, 64);

            Assert.Empty(predicted.Entries);
            Assert.Equal(0, predicted.Stats.StoredItems);
            Assert.Equal(0, predicted.Stats.EntryCount);
            Assert.Equal(0, predicted.MatchingEntries);
        }

        [Fact]
        public void Withdraw_IgnoresUnknownEntry()
        {
            StorageTerminalPage confirmed = Page(quantity: 3, maxStackSize: 64);

            Assert.Same(confirmed, StorageTerminalPrediction.Withdraw(confirmed, 999, 64));
        }

        [Fact]
        public void SingleWithdraw_PredictsExactlyOneItem()
        {
            StorageTerminalPage confirmed = Page(quantity: 40, maxStackSize: 16);

            StorageTerminalPage predicted = StorageTerminalPrediction.Withdraw(confirmed, 7, 1);

            Assert.Equal(39, predicted.Entries[0].Quantity);
            Assert.Equal(39, predicted.Stats.StoredItems);
        }

        [Fact]
        public void Deposit_PredictsCapacityTotalWithoutInventingEntryIdentity()
        {
            StorageTerminalPage confirmed = Page(quantity: 40, maxStackSize: 16);

            StorageTerminalPage predicted = StorageTerminalPrediction.Deposit(confirmed, 5);

            Assert.Equal(45, predicted.Stats.StoredItems);
            Assert.Equal(7, Assert.Single(predicted.Entries).EntryId);
            Assert.Equal(40, predicted.Entries[0].Quantity);
        }

        private static StorageTerminalPage Page(int quantity, int maxStackSize)
        {
            ItemStack stack = StorageTestStacks.Create(
                "game:predicted-item",
                1,
                maxStackSize);
            var entry = new StoredEntry(
                7,
                ItemKey.FromStack(stack),
                stack,
                quantity,
                "predicted item");
            var stats = new StorageStats(
                quantity,
                256,
                1,
                16,
                StorageState.Online,
                0,
                0);
            return new StorageTerminalPage(
                10,
                4,
                stats,
                "",
                StorageTerminalSort.Name,
                0,
                1,
                1,
                StorageTerminalQuery.PageSize,
                new[] { entry });
        }
    }
}
