using System.Collections.Generic;
using System.Linq;

namespace VintageKinematics.Storage.Topology
{
    public sealed partial class StorageStructureScanner
    {
        private static List<StorageTopologyChunk> FindMissingChunks(
            IStorageTopologySource source,
            StorageStructureScanRequest request)
        {
            HashSet<StorageTopologyChunk> required = new HashSet<StorageTopologyChunk>();
            required.Add(source.GetChunk(request.Controller));
            foreach (StorageTopologyPosition known in request.KnownMembers)
            {
                required.Add(source.GetChunk(known));
            }

            return required
                .Where(chunk => !source.IsChunkLoaded(chunk))
                .OrderBy(chunk => chunk, StorageTopologyOrdering.Chunks)
                .ToList();
        }

        private static List<StorageTopologyPosition> FindOrphanedKnownMembers(
            StorageStructureScanRequest request,
            IReadOnlyCollection<StorageMemberSnapshot> members,
            HashSet<StorageTopologyIssue> issues)
        {
            HashSet<StorageTopologyPosition> discovered = new HashSet<StorageTopologyPosition>(
                members.Select(member => member.Position));
            HashSet<StorageTopologyPosition> orphaned = new HashSet<StorageTopologyPosition>();
            foreach (StorageTopologyPosition known in request.KnownMembers)
            {
                if (IsExcluded(known, request) || discovered.Contains(known)) continue;
                orphaned.Add(known);
                if (StorageTopologyRules.ManhattanDistance(request.Controller, known)
                    > request.Limits.MaxGraphDistance)
                {
                    issues.Add(StorageTopologyIssue.GraphDistanceExceeded);
                }
            }
            if (orphaned.Count > 0) issues.Add(StorageTopologyIssue.OrphanedKnownMember);
            return orphaned.OrderBy(position => position, StorageTopologyOrdering.Positions).ToList();
        }

        private static bool IsExcluded(
            StorageTopologyPosition position,
            StorageStructureScanRequest request)
        {
            return request.ExcludedPosition.HasValue
                && request.ExcludedPosition.Value == position;
        }

        private static StorageStructureSnapshot Snapshot(
            IReadOnlyList<StorageMemberSnapshot> members = null,
            IReadOnlyList<StorageTopologyPosition> orphanedKnownMembers = null,
            IReadOnlyList<StorageMemberSnapshot> conflictingContacts = null,
            IReadOnlyList<StorageTopologyChunk> missingChunks = null,
            IEnumerable<StorageTopologyIssue> issues = null,
            long itemCapacity = 0,
            int typeCapacity = 0,
            int nonControllerMemberCount = 0,
            int maximumGraphDistance = 0)
        {
            StorageMemberSnapshot[] orderedMembers = (members ?? new List<StorageMemberSnapshot>())
                .OrderBy(member => member.Position, StorageTopologyOrdering.Positions)
                .ToArray();
            StorageMemberSnapshot[] orderedForeign =
                (conflictingContacts ?? new List<StorageMemberSnapshot>())
                .OrderBy(member => member.Position, StorageTopologyOrdering.Positions)
                .ToArray();
            StorageTopologyChunk[] orderedMissing =
                (missingChunks ?? new List<StorageTopologyChunk>())
                .Distinct()
                .OrderBy(chunk => chunk, StorageTopologyOrdering.Chunks)
                .ToArray();
            StorageTopologyIssue[] orderedIssues = (issues ?? new List<StorageTopologyIssue>())
                .Distinct()
                .OrderBy(issue => issue)
                .ToArray();

            return new StorageStructureSnapshot(
                orderedMembers,
                orphanedKnownMembers,
                orderedForeign,
                orderedMissing,
                orderedIssues,
                itemCapacity,
                typeCapacity,
                nonControllerMemberCount,
                maximumGraphDistance);
        }
    }
}
