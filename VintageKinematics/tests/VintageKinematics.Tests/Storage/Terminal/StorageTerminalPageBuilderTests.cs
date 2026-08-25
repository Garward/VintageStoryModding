using System.Collections.Generic;
using System.Linq;
using VintageKinematics.Api.Storage;
using VintageKinematics.Storage.Terminal;
using Xunit;

namespace VintageKinematics.Tests.Storage.Terminal
{
    public sealed class StorageTerminalPageBuilderTests
    {
        private readonly StorageTerminalPageBuilder builder = new();

        [Fact]
        public void Build_ClampsSearchAndPageWithoutReturningMoreThanFiftyEntries()
        {
            List<StoredEntry> entries = Enumerable.Range(1, 75)
                .Select(id => Entry(id, "item-" + id.ToString("000"), id))
                .ToList();
            var query = new StorageTerminalQuery(7, new string('x', 80), 99, StorageTerminalSort.Name);

            StorageTerminalPage page = builder.Build(entries, query, Stats(entries), revision: 12);

            Assert.Equal(StorageTerminalQuery.MaxSearchLength, page.Search.Length);
            Assert.Empty(page.Entries);
            Assert.Equal(0, page.Page);
            Assert.Equal(1, page.PageCount);
        }

        [Fact]
        public void Build_PagesExactlyFiftyImmutableEntrySnapshots()
        {
            List<StoredEntry> entries = Enumerable.Range(1, 75)
                .Select(id => Entry(id, "item-" + id.ToString("000"), id))
                .ToList();

            StorageTerminalPage first = builder.Build(
                entries,
                new StorageTerminalQuery(1, "", 0, StorageTerminalSort.Name),
                Stats(entries),
                revision: 5);
            StorageTerminalPage second = builder.Build(
                entries,
                new StorageTerminalQuery(2, "", 1, StorageTerminalSort.Name),
                Stats(entries),
                revision: 5);

            Assert.Equal(50, first.Entries.Count);
            Assert.Equal(25, second.Entries.Count);
            Assert.Equal(2, first.PageCount);
            Assert.Equal(75, first.MatchingEntries);
        }

        [Fact]
        public void Build_SearchesStableCodeAndCachedDisplayText()
        {
            StoredEntry[] entries =
            {
                Entry(1, "game:coppergear", 8, "Copper Gear"),
                Entry(2, "game:tinplate", 5, "Tin Plate")
            };

            StorageTerminalPage byName = builder.Build(
                entries,
                new StorageTerminalQuery(1, "gear", 0, StorageTerminalSort.Name),
                Stats(entries),
                revision: 1);
            StorageTerminalPage byCode = builder.Build(
                entries,
                new StorageTerminalQuery(2, "tinplate", 0, StorageTerminalSort.Name),
                Stats(entries),
                revision: 1);

            Assert.Equal(1, Assert.Single(byName.Entries).EntryId);
            Assert.Equal(2, Assert.Single(byCode.Entries).EntryId);
        }

        [Fact]
        public void Build_QuantitySortHasStableEntryIdTieBreak()
        {
            StoredEntry[] entries =
            {
                Entry(3, "same", 10),
                Entry(1, "same", 10),
                Entry(2, "small", 2)
            };

            StorageTerminalPage page = builder.Build(
                entries,
                new StorageTerminalQuery(1, "", 0, StorageTerminalSort.QuantityDescending),
                Stats(entries),
                revision: 3);

            Assert.Equal(new long[] { 1, 3, 2 }, page.Entries.Select(entry => entry.EntryId));
        }

        [Fact]
        public void Build_UsesBoundedExpandedPageSize()
        {
            List<StoredEntry> entries = Enumerable.Range(1, 140)
                .Select(id => Entry(id, "item-" + id.ToString("000"), id))
                .ToList();

            StorageTerminalPage expanded = builder.Build(
                entries,
                new StorageTerminalQuery(1, "", 0, StorageTerminalSort.Name, 90),
                Stats(entries),
                revision: 1);
            StorageTerminalPage clamped = builder.Build(
                entries,
                new StorageTerminalQuery(2, "", 0, StorageTerminalSort.Name, 999),
                Stats(entries),
                revision: 1);

            Assert.Equal(90, expanded.PageSize);
            Assert.Equal(90, expanded.Entries.Count);
            Assert.Equal(StorageTerminalQuery.MaxPageSize, clamped.PageSize);
            Assert.Equal(StorageTerminalQuery.MaxPageSize, clamped.Entries.Count);
        }

        private static StoredEntry Entry(long id, string code, long quantity, string search = "")
        {
            return new StoredEntry(
                id,
                new ItemKey(Vintagestory.API.Common.EnumItemClass.Item, code, 0, 0),
                null,
                quantity,
                search);
        }

        private static StorageStats Stats(IReadOnlyCollection<StoredEntry> entries)
        {
            return new StorageStats(
                entries.Sum(entry => entry.Quantity),
                10000,
                entries.Count,
                0,
                StorageState.Online,
                0,
                0);
        }
    }
}
