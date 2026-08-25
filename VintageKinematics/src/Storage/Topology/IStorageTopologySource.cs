namespace VintageKinematics.Storage.Topology
{
    /// <summary>
    /// Read-only world boundary. Implementations must never force-load chunks.
    /// </summary>
    public interface IStorageTopologySource
    {
        StorageTopologyChunk GetChunk(StorageTopologyPosition position);
        bool IsChunkLoaded(StorageTopologyChunk chunk);
        bool TryGetMember(StorageTopologyPosition position, out StorageMemberSnapshot member);
    }
}
