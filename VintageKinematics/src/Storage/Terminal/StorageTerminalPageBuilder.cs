using System;
using System.Collections.Generic;
using System.Linq;
using VintageKinematics.Api.Storage;

namespace VintageKinematics.Storage.Terminal
{
    /// <summary>Pure query layer over immutable index snapshots; never scans world storage.</summary>
    public sealed class StorageTerminalPageBuilder
    {
        public StorageTerminalPage Build(
            IReadOnlyCollection<StoredEntry> source,
            StorageTerminalQuery query,
            StorageStats stats,
            long revision)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            IEnumerable<StoredEntry> matching = source ?? Array.Empty<StoredEntry>();
            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                string needle = query.Search.ToLowerInvariant();
                matching = matching.Where(entry => SearchText(entry).Contains(
                    needle,
                    StringComparison.Ordinal));
            }

            matching = query.Sort switch
            {
                StorageTerminalSort.QuantityDescending => matching
                    .OrderByDescending(entry => entry.Quantity)
                    .ThenBy(DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.EntryId),
                StorageTerminalSort.QuantityAscending => matching
                    .OrderBy(entry => entry.Quantity)
                    .ThenBy(DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.EntryId),
                _ => matching
                    .OrderBy(DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.EntryId)
            };

            List<StoredEntry> ordered = matching.ToList();
            int pageSize = query.RequestedPageSize;
            int pageCount = Math.Max(1, (ordered.Count + pageSize - 1) / pageSize);
            int page = Math.Min(query.Page, pageCount - 1);
            StoredEntry[] entries = ordered
                .Skip(page * pageSize)
                .Take(pageSize)
                .ToArray();

            return new StorageTerminalPage(
                query.RequestId,
                revision,
                stats,
                query.Search,
                query.Sort,
                page,
                pageCount,
                ordered.Count,
                pageSize,
                entries);
        }

        private static string SearchText(StoredEntry entry)
        {
            if (entry == null) return string.Empty;
            string name = DisplayName(entry);
            string code = entry.Key.Code ?? string.Empty;
            return (entry.CachedSearchText + "\n" + name + "\n" + code).ToLowerInvariant();
        }

        private static string DisplayName(StoredEntry entry)
        {
            if (entry == null) return string.Empty;
            try
            {
                return entry.Exemplar?.GetName() ?? entry.Key.Code ?? string.Empty;
            }
            catch
            {
                return entry.Key.Code ?? string.Empty;
            }
        }
    }
}
