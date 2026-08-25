using System;
using System.Collections.Generic;
using System.Linq;

namespace VintageKinematics.Storage.Topology
{
    /// <summary>
    /// Bounded face-connected traversal independent from block-entity lifecycle.
    /// </summary>
    public sealed partial class StorageStructureScanner
    {
        public StorageStructureSnapshot Scan(
            IStorageTopologySource source,
            StorageStructureScanRequest request)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (request == null) throw new ArgumentNullException(nameof(request));

            List<StorageTopologyChunk> missingChunks = FindMissingChunks(source, request);
            if (missingChunks.Count > 0)
            {
                return Snapshot(
                    missingChunks: missingChunks,
                    issues: new[] { StorageTopologyIssue.RequiredChunkUnavailable });
            }

            if (IsExcluded(request.Controller, request)
                || !source.TryGetMember(request.Controller, out StorageMemberSnapshot controller))
            {
                return Snapshot(issues: new[] { StorageTopologyIssue.ControllerMissing });
            }
            if (controller.Position != request.Controller)
            {
                return Snapshot(issues: new[] { StorageTopologyIssue.MemberPositionMismatch });
            }
            if (controller.WarehouseId != request.WarehouseId)
            {
                return Snapshot(issues: new[] { StorageTopologyIssue.ControllerWarehouseMismatch });
            }
            if (!controller.IsController)
            {
                return Snapshot(issues: new[] { StorageTopologyIssue.ControllerRoleMismatch });
            }
            if (controller.Controller != request.Controller)
            {
                return Snapshot(issues: new[] { StorageTopologyIssue.ControllerReferenceMismatch });
            }

            List<StorageMemberSnapshot> members = new List<StorageMemberSnapshot>();
            List<StorageMemberSnapshot> conflictingContacts = new List<StorageMemberSnapshot>();
            HashSet<StorageTopologyPosition> visited = new HashSet<StorageTopologyPosition>();
            Queue<ScanNode> open = new Queue<ScanNode>();
            HashSet<StorageTopologyIssue> issues = new HashSet<StorageTopologyIssue>();
            open.Enqueue(new ScanNode(request.Controller, 0));
            long itemCapacity = 0;
            int typeCapacity = 0;
            int nonControllerCount = 0;
            int maximumDistance = 0;

            while (open.Count > 0)
            {
                ScanNode node = open.Dequeue();
                if (!visited.Add(node.Position) || IsExcluded(node.Position, request)) continue;
                StorageTopologyChunk nodeChunk = source.GetChunk(node.Position);
                if (!source.IsChunkLoaded(nodeChunk))
                {
                    missingChunks.Add(nodeChunk);
                    continue;
                }
                if (!source.TryGetMember(node.Position, out StorageMemberSnapshot member)) continue;
                if (member.Position != node.Position)
                {
                    issues.Add(StorageTopologyIssue.MemberPositionMismatch);
                    continue;
                }
                if (!member.IsLinked)
                {
                    conflictingContacts.Add(member);
                    issues.Add(StorageTopologyIssue.UnlinkedMemberContact);
                    continue;
                }
                if (member.WarehouseId != request.WarehouseId)
                {
                    conflictingContacts.Add(member);
                    issues.Add(StorageTopologyIssue.ForeignWarehouseContact);
                    continue;
                }
                if (member.Controller != request.Controller)
                {
                    conflictingContacts.Add(member);
                    issues.Add(StorageTopologyIssue.ControllerReferenceMismatch);
                    continue;
                }

                if (member.IsController)
                {
                    if (member.Position != request.Controller)
                    {
                        issues.Add(StorageTopologyIssue.UnexpectedController);
                    }
                }
                else if (nonControllerCount >= request.Limits.MaxNonControllerMembers)
                {
                    issues.Add(StorageTopologyIssue.MemberLimitExceeded);
                    break;
                }

                if (!TryAddCapacity(member, ref itemCapacity, ref typeCapacity, issues)) break;
                members.Add(member);
                maximumDistance = Math.Max(maximumDistance, node.Distance);
                if (!member.IsController) nonControllerCount++;
                if (node.Distance >= request.Limits.MaxGraphDistance) continue;
                foreach (StorageTopologyPosition neighbor in StorageTopologyRules.FaceNeighbors(node.Position))
                {
                    if (!visited.Contains(neighbor)) open.Enqueue(new ScanNode(neighbor, node.Distance + 1));
                }
            }

            if (missingChunks.Count > 0)
            {
                return Snapshot(
                    missingChunks: missingChunks,
                    issues: new[] { StorageTopologyIssue.RequiredChunkUnavailable });
            }

            List<StorageTopologyPosition> orphaned = FindOrphanedKnownMembers(
                request,
                members,
                issues);
            return Snapshot(
                members,
                orphaned,
                conflictingContacts,
                issues: issues,
                itemCapacity: itemCapacity,
                typeCapacity: typeCapacity,
                nonControllerMemberCount: nonControllerCount,
                maximumGraphDistance: maximumDistance);
        }

        private static bool TryAddCapacity(
            StorageMemberSnapshot member,
            ref long itemCapacity,
            ref int typeCapacity,
            HashSet<StorageTopologyIssue> issues)
        {
            long nextItemCapacity;
            try
            {
                nextItemCapacity = checked(itemCapacity + member.CapacityContribution);
            }
            catch (OverflowException)
            {
                issues.Add(StorageTopologyIssue.CapacityOverflow);
                return false;
            }

            int nextTypeCapacity;
            try
            {
                nextTypeCapacity = checked(typeCapacity + member.TypeCapacityContribution);
            }
            catch (OverflowException)
            {
                issues.Add(StorageTopologyIssue.TypeCapacityOverflow);
                return false;
            }

            itemCapacity = nextItemCapacity;
            typeCapacity = nextTypeCapacity;
            return true;
        }

        private readonly struct ScanNode
        {
            public StorageTopologyPosition Position { get; }
            public int Distance { get; }

            public ScanNode(StorageTopologyPosition position, int distance)
            {
                Position = position;
                Distance = distance;
            }
        }
    }
}
