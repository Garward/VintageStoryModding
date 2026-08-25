using System;
using System.Security.Cryptography;

namespace VintageKinematics.Storage.Recovery
{
    internal static class StorageRecoveryBytes
    {
        public static bool Equal(byte[] left, byte[] right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Length != right.Length) return false;
            return CryptographicOperations.FixedTimeEquals(left, right);
        }
    }
}
