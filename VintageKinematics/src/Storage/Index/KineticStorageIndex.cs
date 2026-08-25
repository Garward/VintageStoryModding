using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using VintageKinematics.Api.Storage;
using VintageKinematics.Storage.Acceptance;

namespace VintageKinematics.Storage.Index
{
    /// <summary>
    /// Collision-safe in-memory item index. Persistence, networking, and warehouse topology
    /// are intentionally owned by separate layers.
    /// </summary>
    public sealed partial class KineticStorageIndex
    {
        private readonly Dictionary<ItemKey, List<InternalStoredEntry>> collisionBuckets = new();
        private readonly Dictionary<long, InternalStoredEntry> entriesById = new();
        private readonly IStorageAcceptanceValidator acceptanceValidator;
        private readonly System.Func<ItemStack, ItemKey> keyFactory;
        private readonly System.Func<ItemStack, ItemStack, bool> exactMatch;

        private StorageIndexLimits limits;
        private long nextEntryId = 1;
        private long resolvedItems;
        private long unresolvedItems;
        private int unresolvedEntries;

        public long StoredItems => checked(resolvedItems + unresolvedItems);
        public long ResolvedItems => resolvedItems;
        public long UnresolvedItems => unresolvedItems;
        public int EntryCount => checked(entriesById.Count + unresolvedEntries);
        public int ResolvedEntryCount => entriesById.Count;
        public int UnresolvedEntryCount => unresolvedEntries;
        public StorageIndexLimits Limits => limits;

        public KineticStorageIndex(
            IWorldAccessor world,
            StorageIndexLimits limits,
            IStorageAcceptanceValidator acceptanceValidator = null)
            : this(
                limits,
                acceptanceValidator ?? new KineticStorageAcceptanceValidator(),
                VKStorageKeys.KeyFor,
                (left, right) => VKStorageKeys.CanAggregate(world, left, right))
        {
        }

        internal KineticStorageIndex(
            StorageIndexLimits limits,
            IStorageAcceptanceValidator acceptanceValidator,
            System.Func<ItemStack, ItemKey> keyFactory,
            System.Func<ItemStack, ItemStack, bool> exactMatch)
        {
            this.acceptanceValidator = acceptanceValidator
                ?? throw new ArgumentNullException(nameof(acceptanceValidator));
            this.keyFactory = keyFactory ?? throw new ArgumentNullException(nameof(keyFactory));
            this.exactMatch = exactMatch ?? throw new ArgumentNullException(nameof(exactMatch));
            this.limits = limits;
        }

        public void UpdateLimits(StorageIndexLimits newLimits)
        {
            limits = newLimits;
        }

        public IReadOnlyCollection<StoredEntry> GetEntries()
        {
            List<StoredEntry> snapshots = new List<StoredEntry>(entriesById.Count);
            foreach (InternalStoredEntry entry in entriesById.Values)
            {
                snapshots.Add(entry.Snapshot());
            }
            snapshots.Sort((left, right) => left.EntryId.CompareTo(right.EntryId));
            return snapshots;
        }

        public bool TryGetEntry(long entryId, out StoredEntry entry)
        {
            if (entriesById.TryGetValue(entryId, out InternalStoredEntry internalEntry))
            {
                entry = internalEntry.Snapshot();
                return true;
            }

            entry = null;
            return false;
        }

        /// <summary>Finds the next matching entry by stable id, wrapping once for fair automation.</summary>
        public bool TryFindNextEntry(
            long afterEntryId,
            System.Func<ItemStack, bool> matches,
            out StoredEntry entry)
        {
            InternalStoredEntry next = null;
            InternalStoredEntry wrapped = null;
            foreach (InternalStoredEntry candidate in entriesById.Values)
            {
                if (matches != null && !matches(candidate.Exemplar)) continue;
                if (wrapped == null || candidate.EntryId < wrapped.EntryId) wrapped = candidate;
                if (candidate.EntryId > afterEntryId
                    && (next == null || candidate.EntryId < next.EntryId))
                {
                    next = candidate;
                }
            }

            InternalStoredEntry selected = next ?? wrapped;
            entry = selected?.Snapshot();
            return entry != null;
        }

    }
}
