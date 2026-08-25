using System.IO;
using System.Text;

namespace VintageKinematics.Storage.Recovery
{
    internal static class StorageRecoveryIndexCommitCodec
    {
        public static byte[] Encode(long generation, byte[] indexBytes)
        {
            StorageRecoveryIndexCommit commit = new StorageRecoveryIndexCommit(generation, indexBytes);
            using MemoryStream stream = new MemoryStream();
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(StorageRecoveryConstants.IndexCommitMagic);
                writer.Write(StorageRecoveryConstants.IndexCommitSchemaVersion);
                writer.Write(commit.Generation);
                writer.Write(indexBytes.Length);
                writer.Write(StorageRecoveryChecksum.Compute(indexBytes));
                writer.Write(indexBytes);
            }
            byte[] encoded = stream.ToArray();
            if (encoded.Length > StorageRecoveryConstants.MaxIndexCommitBytes)
            {
                throw new InvalidDataException("Recovery index commit is too large.");
            }
            return encoded;
        }

        public static bool TryDecode(byte[] bytes, out StorageRecoveryIndexCommit commit)
        {
            commit = null;
            if (bytes == null || bytes.Length == 0
                || bytes.Length > StorageRecoveryConstants.MaxIndexCommitBytes)
            {
                return false;
            }
            try
            {
                using MemoryStream stream = new MemoryStream(bytes, writable: false);
                using BinaryReader reader = new BinaryReader(stream, Encoding.UTF8);
                if (reader.ReadUInt32() != StorageRecoveryConstants.IndexCommitMagic) return false;
                if (reader.ReadInt32() != StorageRecoveryConstants.IndexCommitSchemaVersion) return false;
                long generation = reader.ReadInt64();
                int length = reader.ReadInt32();
                if (generation <= 0 || length <= 0 || length > StorageRecoveryConstants.MaxIndexBytes)
                {
                    return false;
                }
                byte[] checksum = reader.ReadBytes(StorageRecoveryChecksum.Size);
                byte[] indexBytes = reader.ReadBytes(length);
                if (checksum.Length != StorageRecoveryChecksum.Size
                    || indexBytes.Length != length
                    || stream.Position != stream.Length
                    || !StorageRecoveryChecksum.Matches(indexBytes, checksum)
                    || !StorageRecoveryRegistryCodec.DecodeIndex(indexBytes).Success)
                {
                    return false;
                }
                commit = new StorageRecoveryIndexCommit(generation, indexBytes);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
