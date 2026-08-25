using System;

namespace VintageKinematics.Storage.Topology
{
    public readonly struct StorageTopologyPosition : IEquatable<StorageTopologyPosition>
    {
        public int X { get; }
        public int InternalY { get; }
        public int Z { get; }
        public int Dimension { get; }

        public StorageTopologyPosition(int x, int internalY, int z, int dimension)
        {
            X = x;
            InternalY = internalY;
            Z = z;
            Dimension = dimension;
        }

        public StorageTopologyPosition Offset(int x, int y, int z)
        {
            return new StorageTopologyPosition(X + x, InternalY + y, Z + z, Dimension);
        }

        public bool Equals(StorageTopologyPosition other)
        {
            return X == other.X
                && InternalY == other.InternalY
                && Z == other.Z
                && Dimension == other.Dimension;
        }

        public override bool Equals(object obj)
        {
            return obj is StorageTopologyPosition other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(X, InternalY, Z, Dimension);
        }

        public static bool operator ==(StorageTopologyPosition left, StorageTopologyPosition right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(StorageTopologyPosition left, StorageTopologyPosition right)
        {
            return !left.Equals(right);
        }
    }
}
