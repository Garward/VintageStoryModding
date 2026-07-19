using Vintagestory.API.Common;

namespace VintageKinematics.Api.Storage
{
    /// <summary>
    /// Aggregated view of one stored item key. Implementations own the exemplar clone.
    /// </summary>
    public sealed class StoredEntry
    {
        public ItemKey Key { get; set; }
        public ItemStack Exemplar { get; set; }
        public long Quantity { get; set; }
        public string CachedSearchText { get; set; }

        public StoredEntry(ItemKey key, ItemStack exemplar, long quantity, string cachedSearchText = null)
        {
            Key = key;
            Exemplar = exemplar;
            Quantity = quantity;
            CachedSearchText = cachedSearchText ?? string.Empty;
        }

        public StoredEntry CloneShallow()
        {
            return new StoredEntry(Key, Exemplar?.Clone(), Quantity, CachedSearchText);
        }
    }
}
