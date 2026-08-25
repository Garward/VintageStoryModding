using Vintagestory.API.Common;

namespace VintageKinematics.Api.Storage
{
    /// <summary>
    /// Immutable public snapshot of one aggregated stored entry.
    /// Implementations must not expose their mutable internal entry objects through the API.
    /// </summary>
    public sealed class StoredEntry
    {
        private readonly ItemStack exemplar;

        public long EntryId { get; }
        public ItemKey Key { get; }
        public ItemStack Exemplar => exemplar?.Clone();
        public long Quantity { get; }
        public string CachedSearchText { get; }

        public StoredEntry(long entryId, ItemKey key, ItemStack exemplar, long quantity, string cachedSearchText = null)
        {
            EntryId = entryId;
            Key = key;
            this.exemplar = exemplar?.Clone();
            Quantity = quantity;
            CachedSearchText = cachedSearchText ?? string.Empty;
        }

        public StoredEntry CloneSnapshot()
        {
            return new StoredEntry(EntryId, Key, exemplar, Quantity, CachedSearchText);
        }
    }
}
