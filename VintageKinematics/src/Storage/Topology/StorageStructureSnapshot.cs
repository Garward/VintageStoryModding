using System;
using System.Collections.Generic;

namespace VintageKinematics.Storage.Topology
{
    /// <summary>
    /// Immutable result of one bounded structure scan.
    /// </summary>
    public sealed class StorageStructureSnapshot
    {
        private readonly IReadOnlyList<StorageMemberSnapshot> members;
        private readonly IReadOnlyList<StorageTopologyPosition> orphanedKnownMembers;
        private readonly IReadOnlyList<StorageMemberSnapshot> conflictingContacts;
        private readonly IReadOnlyList<StorageTopologyChunk> missingChunks;
        private readonly IReadOnlyList<StorageTopologyIssue> issues;

        public IReadOnlyList<StorageMemberSnapshot> Members => members;
        public IReadOnlyList<StorageTopologyPosition> OrphanedKnownMembers => orphanedKnownMembers;
        public IReadOnlyList<StorageMemberSnapshot> ConflictingContacts => conflictingContacts;
        public IReadOnlyList<StorageTopologyChunk> MissingChunks => missingChunks;
        public IReadOnlyList<StorageTopologyIssue> Issues => issues;
        public long ItemCapacity { get; }
        public int TypeCapacity { get; }
        public int NonControllerMemberCount { get; }
        public int MaximumGraphDistance { get; }
        public bool IsComplete => missingChunks.Count == 0;
        public bool IsValid => issues.Count == 0;

        internal StorageStructureSnapshot(
            IReadOnlyList<StorageMemberSnapshot> members,
            IReadOnlyList<StorageTopologyPosition> orphanedKnownMembers,
            IReadOnlyList<StorageMemberSnapshot> conflictingContacts,
            IReadOnlyList<StorageTopologyChunk> missingChunks,
            IReadOnlyList<StorageTopologyIssue> issues,
            long itemCapacity,
            int typeCapacity,
            int nonControllerMemberCount,
            int maximumGraphDistance)
        {
            this.members = Copy(members);
            this.orphanedKnownMembers = Copy(orphanedKnownMembers);
            this.conflictingContacts = Copy(conflictingContacts);
            this.missingChunks = Copy(missingChunks);
            this.issues = Copy(issues);
            ItemCapacity = itemCapacity;
            TypeCapacity = typeCapacity;
            NonControllerMemberCount = nonControllerMemberCount;
            MaximumGraphDistance = maximumGraphDistance;
        }

        private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> source)
        {
            if (source == null) return Array.Empty<T>();
            T[] copy = new T[source.Count];
            for (int i = 0; i < source.Count; i++) copy[i] = source[i];
            return copy;
        }
    }
}
