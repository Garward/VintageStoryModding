namespace VintageKinematics.BlockEntities.Storage
{
    /// <summary>
    /// Capacity-only member. It stores a controller link but never item contents.
    /// </summary>
    public partial class BEKineticWarehouseCell : BEKineticStorageMember
    {
        public override bool IsController => false;
        public override long CapacityContribution => ResolveCapacity();

        private long ResolveCapacity()
        {
            int fallback = Block?.Variant?["material"] == "reinforced" ? 4096 : 1024;
            return System.Math.Max(
                0,
                Block?.Attributes?["vkStorageCapacity"].AsInt(fallback) ?? fallback);
        }
    }
}
