using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using VintageKinematics.Api.Storage;

namespace VintageKinematics.Storage.Terminal
{
    /// <summary>
    /// Builds disposable client views for immediate feedback. Predictions are never persisted
    /// or sent as storage facts; the next server page always replaces them.
    /// </summary>
    public static class StorageTerminalPrediction
    {
        public static StorageTerminalPage Withdraw(
            StorageTerminalPage confirmed,
            long entryId,
            int requestedQuantity)
        {
            if (confirmed == null || entryId <= 0 || requestedQuantity <= 0) return confirmed;

            var entries = new List<StoredEntry>(confirmed.Entries.Count);
            int moved = 0;
            bool removedEntry = false;
            foreach (StoredEntry entry in confirmed.Entries)
            {
                if (entry == null || entry.EntryId != entryId)
                {
                    entries.Add(entry);
                    continue;
                }

                ItemStack exemplar = entry.Exemplar;
                moved = (int)Math.Min(entry.Quantity, requestedQuantity);
                long remaining = entry.Quantity - moved;
                if (moved <= 0)
                {
                    entries.Add(entry);
                }
                else if (remaining > 0)
                {
                    entries.Add(new StoredEntry(
                        entry.EntryId,
                        entry.Key,
                        exemplar,
                        remaining,
                        entry.CachedSearchText));
                }
                else
                {
                    removedEntry = true;
                }
            }
            if (moved <= 0) return confirmed;

            StorageStats stats = confirmed.Stats;
            var predictedStats = new StorageStats(
                Math.Max(0, stats.StoredItems - moved),
                stats.ItemCapacity,
                Math.Max(0, stats.EntryCount - (removedEntry ? 1 : 0)),
                stats.TypeCapacity,
                stats.State,
                stats.ImportRate,
                stats.ExportRate,
                stats.PowerRequired,
                stats.Powered);
            int matchingEntries = Math.Max(
                0,
                confirmed.MatchingEntries - (removedEntry ? 1 : 0));
            int pageCount = Math.Max(
                1,
                (matchingEntries + confirmed.PageSize - 1) / confirmed.PageSize);

            return new StorageTerminalPage(
                confirmed.RequestId,
                confirmed.Revision,
                predictedStats,
                confirmed.Search,
                confirmed.Sort,
                Math.Min(confirmed.Page, pageCount - 1),
                pageCount,
                matchingEntries,
                confirmed.PageSize,
                entries);
        }

        public static StorageTerminalPage Deposit(StorageTerminalPage confirmed, int quantity)
        {
            if (confirmed == null || quantity <= 0) return confirmed;
            StorageStats stats = confirmed.Stats;
            var predictedStats = new StorageStats(
                Math.Min(stats.ItemCapacity, stats.StoredItems + quantity),
                stats.ItemCapacity,
                stats.EntryCount,
                stats.TypeCapacity,
                stats.State,
                stats.ImportRate,
                stats.ExportRate,
                stats.PowerRequired,
                stats.Powered);
            return new StorageTerminalPage(
                confirmed.RequestId,
                confirmed.Revision,
                predictedStats,
                confirmed.Search,
                confirmed.Sort,
                confirmed.Page,
                confirmed.PageCount,
                confirmed.MatchingEntries,
                confirmed.PageSize,
                confirmed.Entries);
        }
    }
}
