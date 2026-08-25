using Vintagestory.API.Common;
using VintageKinematics.Storage.Acceptance;

namespace VintageKinematics.Storage.Persistence
{
    /// <summary>
    /// Entry snapshot persistence boundary. Save-game registry ownership remains in the
    /// future recovery system; this class only translates an index to and from bytes.
    /// </summary>
    public sealed partial class KineticStoragePersistence
    {
        private readonly IWorldAccessor world;
        private readonly IStorageCollectibleResolver collectibleResolver;
        private readonly IStorageAcceptanceValidator acceptanceValidator;

        public KineticStoragePersistence(
            IWorldAccessor world,
            IStorageCollectibleResolver collectibleResolver = null,
            IStorageAcceptanceValidator acceptanceValidator = null)
        {
            this.world = world;
            this.collectibleResolver = collectibleResolver ?? new WorldStorageCollectibleResolver(world);
            this.acceptanceValidator = acceptanceValidator ?? new KineticStorageAcceptanceValidator();
        }

        public byte[] Encode(StoragePersistenceSnapshot snapshot)
        {
            return StorageSnapshotCodec.Encode(snapshot);
        }
    }
}
