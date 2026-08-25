using System;

namespace VintageKinematics.Storage.Recovery
{
    /// <summary>
    /// Immutable persisted location of a warehouse controller.
    /// Y is the controller's internal world Y coordinate, not dimension-local Y.
    /// </summary>
    public readonly struct StorageControllerLocation : IEquatable<StorageControllerLocation>
    {
        public int X { get; }
        public int InternalY { get; }
        public int Z { get; }
        public int Dimension { get; }

        public StorageControllerLocation(int x, int internalY, int z, int dimension)
        {
            X = x;
            InternalY = internalY;
            Z = z;
            Dimension = dimension;
        }

        public bool Equals(StorageControllerLocation other)
        {
            return X == other.X
                && InternalY == other.InternalY
                && Z == other.Z
                && Dimension == other.Dimension;
        }

        public override bool Equals(object obj)
        {
            return obj is StorageControllerLocation other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(X, InternalY, Z, Dimension);
        }

        public static bool operator ==(StorageControllerLocation left, StorageControllerLocation right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(StorageControllerLocation left, StorageControllerLocation right)
        {
            return !left.Equals(right);
        }
    }
}
