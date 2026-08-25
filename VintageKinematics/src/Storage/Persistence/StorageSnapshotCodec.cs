using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace VintageKinematics.Storage.Persistence
{
    internal static partial class StorageSnapshotCodec
    {
        public static byte[] Encode(StoragePersistenceSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.SchemaVersion != StoragePersistenceConstants.SchemaVersion)
            {
                throw new InvalidOperationException("Unsupported storage schema version.");
            }
            if (snapshot.Entries.Count > StoragePersistenceConstants.MaxSnapshotEntries)
            {
                throw new InvalidOperationException("Storage entry limit exceeded.");
            }

            IReadOnlyList<PersistedStorageEntry> entries = snapshot.Entries
                .OrderBy(entry => entry.EntryId)
                .ToArray();
            using MemoryStream stream = new MemoryStream();
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(StoragePersistenceConstants.SnapshotMagic);
                writer.Write(snapshot.SchemaVersion);
                writer.Write(snapshot.NextEntryId);
                writer.Write(entries.Count);
                foreach (PersistedStorageEntry entry in entries)
                {
                    byte[] raw = entry.RawRecordBytes;
                    writer.Write(raw.Length);
                    writer.Write(raw);
                }
            }

            byte[] encoded = stream.ToArray();
            if (encoded.Length > StoragePersistenceConstants.MaxSnapshotBytes)
            {
                throw new InvalidOperationException("Storage snapshot is too large.");
            }
            return encoded;
        }
    }
}
