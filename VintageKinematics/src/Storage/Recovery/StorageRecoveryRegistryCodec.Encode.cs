using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace VintageKinematics.Storage.Recovery
{
    internal static partial class StorageRecoveryRegistryCodec
    {
        public static byte[] EncodeIndex(IReadOnlyCollection<StorageRecoveryIndexEntry> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            if (entries.Count > StorageRecoveryConstants.MaxWarehouses)
            {
                throw new InvalidOperationException("Recovery warehouse limit exceeded.");
            }

            StorageRecoveryIndexEntry[] ordered = entries
                .OrderBy(entry => entry.WarehouseId, StringComparer.Ordinal)
                .ToArray();
            if (ordered.Select(entry => entry.WarehouseId).Distinct(StringComparer.Ordinal).Count()
                != ordered.Length)
            {
                throw new InvalidOperationException("Duplicate warehouse id in recovery index.");
            }

            using MemoryStream stream = new MemoryStream();
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(StorageRecoveryConstants.IndexMagic);
                writer.Write(StorageRecoveryConstants.IndexSchemaVersion);
                writer.Write(ordered.Length);
                foreach (StorageRecoveryIndexEntry entry in ordered) WriteIndexEntry(writer, entry);
            }

            byte[] bytes = stream.ToArray();
            if (bytes.Length > StorageRecoveryConstants.MaxIndexBytes)
            {
                throw new InvalidOperationException("Recovery index is too large.");
            }
            return bytes;
        }

        public static byte[] EncodeRecord(StorageRecoveryRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            byte[] snapshot = record.SnapshotBytes;
            if (snapshot.Length > StorageRecoveryConstants.MaxSnapshotBytes)
            {
                throw new InvalidOperationException("Recovery snapshot is too large.");
            }

            using MemoryStream stream = new MemoryStream();
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(StorageRecoveryConstants.RecordMagic);
                writer.Write(StorageRecoveryConstants.RecordSchemaVersion);
                WriteWarehouseId(writer, record.WarehouseId);
                WriteController(writer, record.Controller);
                writer.Write(record.Revision);
                writer.Write(record.IsTombstone);
                writer.Write(record.Checksum);
                writer.Write(snapshot.Length);
                writer.Write(snapshot);
            }

            byte[] bytes = stream.ToArray();
            if (bytes.Length > StorageRecoveryConstants.MaxRecordBytes)
            {
                throw new InvalidOperationException("Recovery record is too large.");
            }
            return bytes;
        }

        private static void WriteIndexEntry(BinaryWriter writer, StorageRecoveryIndexEntry entry)
        {
            if (entry == null) throw new InvalidOperationException("Recovery index contains a null entry.");
            WriteWarehouseId(writer, entry.WarehouseId);
            WriteController(writer, entry.Controller);
            writer.Write(entry.Revision);
            writer.Write(entry.IsTombstone);
            writer.Write(entry.Checksum);
            writer.Write((byte)(entry.RecordSlot < 0 ? byte.MaxValue : entry.RecordSlot));
        }

        private static void WriteWarehouseId(BinaryWriter writer, string warehouseId)
        {
            writer.Write(Guid.Parse(StorageWarehouseId.Normalize(warehouseId)).ToByteArray());
        }

        private static void WriteController(BinaryWriter writer, StorageControllerLocation controller)
        {
            writer.Write(controller.X);
            writer.Write(controller.InternalY);
            writer.Write(controller.Z);
            writer.Write(controller.Dimension);
        }
    }
}
