using System;

namespace VintageKinematics.Storage.Topology
{
    public readonly struct StorageTopologyLimits
    {
        public const int DefaultMaxGraphDistance = 16;
        public const int DefaultMaxNonControllerMembers = 256;

        public int MaxGraphDistance { get; }
        public int MaxNonControllerMembers { get; }

        public StorageTopologyLimits()
            : this(DefaultMaxGraphDistance, DefaultMaxNonControllerMembers)
        {
        }

        public StorageTopologyLimits(
            int maxGraphDistance,
            int maxNonControllerMembers)
        {
            if (maxGraphDistance < 0) throw new ArgumentOutOfRangeException(nameof(maxGraphDistance));
            if (maxGraphDistance > DefaultMaxGraphDistance)
            {
                throw new ArgumentOutOfRangeException(nameof(maxGraphDistance));
            }
            if (maxNonControllerMembers < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxNonControllerMembers));
            }
            if (maxNonControllerMembers > DefaultMaxNonControllerMembers)
            {
                throw new ArgumentOutOfRangeException(nameof(maxNonControllerMembers));
            }

            MaxGraphDistance = maxGraphDistance;
            MaxNonControllerMembers = maxNonControllerMembers;
        }
    }
}
