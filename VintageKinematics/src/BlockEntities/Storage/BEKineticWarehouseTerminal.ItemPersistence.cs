using Vintagestory.API.Datastructures;
using VintageKinematics.Storage.Recovery;

namespace VintageKinematics.BlockEntities.Storage
{
    public partial class BEKineticWarehouseTerminal
    {
        internal void WriteControllerItemAttributes(ITreeAttribute tree)
        {
            byte[] bytes = null;
            if (activeRecoveryRecord != null)
            {
                bytes = StorageRecoveryRegistryCodec.EncodeIndex(
                    new[] { new StorageRecoveryIndexEntry(activeRecoveryRecord) });
            }
            else if (persistedItemHeaderBytes.Length > 0)
            {
                bytes = persistedItemHeaderBytes;
            }

            if (bytes == null) tree.RemoveAttribute(StorageBlockEntityKeys.RecoveryHeader);
            else tree.SetBytes(StorageBlockEntityKeys.RecoveryHeader, bytes);
        }

        internal void ReadControllerItemAttributes(ITreeAttribute tree)
        {
            byte[] bytes = tree.GetBytes(StorageBlockEntityKeys.RecoveryHeader);
            if (bytes == null || bytes.Length == 0)
            {
                persistedItemCopy = StorageSnapshotCopy.Missing();
                return;
            }

            persistedItemHeaderBytes = (byte[])bytes.Clone();
            StorageRecoveryIndexDecodeResult header =
                StorageRecoveryRegistryCodec.DecodeIndex(bytes);
            if (header.Success && header.Entries.Count == 1)
            {
                persistedItemHeader = header.Entries[0];
                persistedItemCopy = StorageSnapshotCopy.Missing();
                return;
            }

            // Development builds before the dual-mirror design stored the full envelope in
            // the BE. Retain and migrate that evidence rather than discarding it.
            StorageRecoveryRecordDecodeResult legacy =
                StorageRecoveryRegistryCodec.DecodeRecord(bytes);
            persistedItemCopy = legacy.Success
                ? StorageSnapshotCopy.FromRecord(legacy.Record, bytes)
                : StorageSnapshotCopy.Invalid(bytes);
        }
    }
}
