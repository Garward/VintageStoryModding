using System;
using System.Security.Cryptography;

namespace VintageKinematics.Storage.Recovery
{
    internal static class StorageRecoveryChecksum
    {
        public const int Size = 32;

        public static byte[] Compute(byte[] snapshotBytes)
        {
            if (snapshotBytes == null) throw new ArgumentNullException(nameof(snapshotBytes));
            return SHA256.HashData(snapshotBytes);
        }

        public static bool Matches(byte[] snapshotBytes, byte[] expectedChecksum)
        {
            if (snapshotBytes == null || expectedChecksum == null || expectedChecksum.Length != Size)
            {
                return false;
            }

            byte[] actualChecksum = Compute(snapshotBytes);
            return CryptographicOperations.FixedTimeEquals(actualChecksum, expectedChecksum);
        }
    }
}
