namespace VintageKinematics.BlockEntities.Storage
{
    /// <summary>
    /// Serializes persisted warehouse state against gameplay mutations. Stratum may encode
    /// dirty chunks off-thread, so every persisted mutation shares this narrow per-terminal gate.
    /// </summary>
    public partial class BEKineticWarehouseTerminal
    {
        private readonly object persistenceSync = new object();
    }
}
