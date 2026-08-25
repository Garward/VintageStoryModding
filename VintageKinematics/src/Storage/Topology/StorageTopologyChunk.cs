using System;

namespace VintageKinematics.Storage.Topology
{
    public readonly struct StorageTopologyChunk : IEquatable<StorageTopologyChunk>
    {
        public int X { get; }
        public int Y { get; }
        public int Z { get; }
        public int Dimension { get; }

        public StorageTopologyChunk(int x, int y, int z, int dimension)
        {
            X = x;
            Y = y;
            Z = z;
            Dimension = dimension;
        }

        public bool Equals(StorageTopologyChunk other)
        {
            return X == other.X && Y == other.Y && Z == other.Z && Dimension == other.Dimension;
        }

        public override bool Equals(object obj)
        {
            return obj is StorageTopologyChunk other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y, Z, Dimension);
        }
    }
}
