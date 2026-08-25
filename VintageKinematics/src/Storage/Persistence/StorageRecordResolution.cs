using Vintagestory.API.Common;

namespace VintageKinematics.Storage.Persistence
{
    internal enum StorageRecordResolutionKind
    {
        Resolved,
        Unresolved,
        Quarantined
    }

    internal readonly struct StorageRecordResolution
    {
        public readonly StorageRecordResolutionKind Kind;
        public readonly ItemStack Exemplar;
        public readonly QuarantinedStorageEntry Quarantine;

        private StorageRecordResolution(
            StorageRecordResolutionKind kind,
            ItemStack exemplar,
            QuarantinedStorageEntry quarantine)
        {
            Kind = kind;
            Exemplar = exemplar;
            Quarantine = quarantine;
        }

        public static StorageRecordResolution Resolved(ItemStack exemplar)
        {
            return new StorageRecordResolution(StorageRecordResolutionKind.Resolved, exemplar, null);
        }

        public static StorageRecordResolution Unresolved()
        {
            return new StorageRecordResolution(StorageRecordResolutionKind.Unresolved, null, null);
        }

        public static StorageRecordResolution Quarantined(QuarantinedStorageEntry quarantine)
        {
            return new StorageRecordResolution(StorageRecordResolutionKind.Quarantined, null, quarantine);
        }
    }
}
