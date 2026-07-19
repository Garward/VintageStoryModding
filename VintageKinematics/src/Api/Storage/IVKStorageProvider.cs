namespace VintageKinematics.Api.Storage
{
    /// <summary>
    /// Implemented by block entities that expose a VK indexed storage network.
    /// </summary>
    public interface IVKStorageProvider
    {
        IVKStorageNetwork StorageNetwork { get; }
    }
}
