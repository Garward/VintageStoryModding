using System;
using System.Collections.Generic;
using VintageKinematics.Api.Storage;

namespace VintageKinematics.Storage.Terminal
{
    /// <summary>Immutable, bounded view of one storage-index revision.</summary>
    public sealed class StorageTerminalPage
    {
        private readonly StoredEntry[] entries;

        public long RequestId { get; }
        public long Revision { get; }
        public StorageStats Stats { get; }
        public string Search { get; }
        public StorageTerminalSort Sort { get; }
        public int Page { get; }
        public int PageCount { get; }
        public int MatchingEntries { get; }
        public int PageSize { get; }
        public IReadOnlyList<StoredEntry> Entries => entries;

        public StorageTerminalPage(
            long requestId,
            long revision,
            StorageStats stats,
            string search,
            StorageTerminalSort sort,
            int page,
            int pageCount,
            int matchingEntries,
            int pageSize,
            IReadOnlyList<StoredEntry> entries)
        {
            RequestId = requestId;
            Revision = Math.Max(0, revision);
            Stats = stats;
            Search = search ?? string.Empty;
            Sort = sort;
            Page = Math.Max(0, page);
            PageCount = Math.Max(1, pageCount);
            MatchingEntries = Math.Max(0, matchingEntries);
            PageSize = Math.Clamp(
                pageSize,
                StorageTerminalQuery.PageSize,
                StorageTerminalQuery.MaxPageSize);
            this.entries = CloneEntries(entries);
        }

        private static StoredEntry[] CloneEntries(IReadOnlyList<StoredEntry> source)
        {
            if (source == null || source.Count == 0) return Array.Empty<StoredEntry>();
            StoredEntry[] copy = new StoredEntry[source.Count];
            for (int index = 0; index < source.Count; index++)
            {
                copy[index] = source[index]?.CloneSnapshot();
            }
            return copy;
        }
    }
}
