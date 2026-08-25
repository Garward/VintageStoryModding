using System;
using System.Collections.Generic;
using System.Linq;
using VintageKinematics.Storage.Topology;
using Xunit;

namespace VintageKinematics.Tests.Storage.Topology
{
    public class StorageStructureScannerTests
    {
        private const string WarehouseId = "cbb91396-f8c6-4992-8447-504841a13ed9";
        private const string OtherWarehouseId = "01476d56-5104-44f0-9ee3-405252f136e6";
        private static readonly StorageTopologyPosition Controller = new(0, 0, 0, 0);

        [Fact]
        public void FaceConnectedMembers_ProduceDeterministicCapacitySnapshot()
        {
            FakeTopologySource source = new FakeTopologySource();
            source.Add(Member(Controller, StorageMemberRole.Controller, 256));
            source.Add(Member(Pos(1, 0, 0), StorageMemberRole.CapacityCell, 1024));
            source.Add(Member(Pos(2, 0, 0), StorageMemberRole.CapacityCell, 4096));
            StorageStructureScanRequest request = Request(source.MemberPositions);

            StorageStructureSnapshot result = new StorageStructureScanner().Scan(source, request);

            Assert.True(result.IsComplete);
            Assert.True(result.IsValid);
            Assert.Equal(5376, result.ItemCapacity);
            Assert.Equal(2, result.NonControllerMemberCount);
            Assert.Equal(2, result.MaximumGraphDistance);
            Assert.Equal(
                new[] { Controller, Pos(1, 0, 0), Pos(2, 0, 0) },
                result.Members.Select(member => member.Position));
            Assert.Empty(result.OrphanedKnownMembers);
        }

        [Fact]
        public void DiagonalContact_DoesNotConnectKnownMember()
        {
            FakeTopologySource source = new FakeTopologySource();
            source.Add(Member(Controller, StorageMemberRole.Controller, 256));
            StorageTopologyPosition diagonal = Pos(1, 1, 0);
            source.Add(Member(diagonal, StorageMemberRole.CapacityCell, 1024));

            StorageStructureSnapshot result = new StorageStructureScanner().Scan(
                source,
                Request(source.MemberPositions));

            Assert.False(result.IsValid);
            Assert.Single(result.Members);
            Assert.Equal(256, result.ItemCapacity);
            Assert.Equal(diagonal, Assert.Single(result.OrphanedKnownMembers));
            Assert.Contains(StorageTopologyIssue.OrphanedKnownMember, result.Issues);
            Assert.False(StorageTopologyRules.AreFaceAdjacent(Controller, diagonal));
        }

        [Fact]
        public void ForeignWarehouseContact_IsReportedButNeverTraversed()
        {
            FakeTopologySource source = new FakeTopologySource();
            source.Add(Member(Controller, StorageMemberRole.Controller, 256));
            StorageMemberSnapshot foreign = Member(
                Pos(1, 0, 0),
                StorageMemberRole.CapacityCell,
                1024,
                OtherWarehouseId);
            source.Add(foreign);

            StorageStructureSnapshot result = new StorageStructureScanner().Scan(
                source,
                Request(new[] { Controller }));

            Assert.False(result.IsValid);
            Assert.Contains(StorageTopologyIssue.ForeignWarehouseContact, result.Issues);
            Assert.Same(foreign, Assert.Single(result.ConflictingContacts));
            Assert.Single(result.Members);
        }

        [Fact]
        public void MemberClaimingDifferentController_IsRejectedAsConflict()
        {
            FakeTopologySource source = new FakeTopologySource();
            source.Add(Member(Controller, StorageMemberRole.Controller, 256));
            StorageMemberSnapshot conflicting = new StorageMemberSnapshot(
                Pos(1, 0, 0),
                WarehouseId,
                StorageMemberRole.CapacityCell,
                1024,
                0,
                "vintagekinematics:storagecell",
                Pos(99, 0, 0));
            source.Add(conflicting);

            StorageStructureSnapshot result = new StorageStructureScanner().Scan(
                source,
                Request(new[] { Controller }));

            Assert.False(result.IsValid);
            Assert.Contains(StorageTopologyIssue.ControllerReferenceMismatch, result.Issues);
            Assert.Same(conflicting, Assert.Single(result.ConflictingContacts));
            Assert.Single(result.Members);
        }

        [Fact]
        public void UnlinkedCellContact_IsReportedWithoutBeingAdopted()
        {
            FakeTopologySource source = new FakeTopologySource();
            source.Add(Member(Controller, StorageMemberRole.Controller, 256));
            StorageMemberSnapshot orphan = new StorageMemberSnapshot(
                Pos(1, 0, 0),
                null,
                StorageMemberRole.CapacityCell,
                1024,
                0,
                "vintagekinematics:storagecell");
            source.Add(orphan);

            StorageStructureSnapshot result = new StorageStructureScanner().Scan(
                source,
                Request(new[] { Controller }));

            Assert.False(result.IsValid);
            Assert.Contains(StorageTopologyIssue.UnlinkedMemberContact, result.Issues);
            Assert.Same(orphan, Assert.Single(result.ConflictingContacts));
            Assert.Equal(256, result.ItemCapacity);
        }

        [Fact]
        public void SecondController_IsRetainedAsInvalidEvidence()
        {
            FakeTopologySource source = new FakeTopologySource();
            source.Add(Member(Controller, StorageMemberRole.Controller, 256));
            source.Add(Member(Pos(1, 0, 0), StorageMemberRole.Controller, 256));

            StorageStructureSnapshot result = new StorageStructureScanner().Scan(
                source,
                Request(source.MemberPositions));

            Assert.False(result.IsValid);
            Assert.Contains(StorageTopologyIssue.UnexpectedController, result.Issues);
            Assert.Equal(2, result.Members.Count);
        }

        [Fact]
        public void MemberBeyondGraphDistance_IsOrphanedAndExcludedFromCapacity()
        {
            FakeTopologySource source = new FakeTopologySource();
            source.Add(Member(Controller, StorageMemberRole.Controller, 256));
            for (int x = 1; x <= 17; x++)
            {
                source.Add(Member(Pos(x, 0, 0), StorageMemberRole.CapacityCell, 1));
            }

            StorageStructureSnapshot result = new StorageStructureScanner().Scan(
                source,
                Request(source.MemberPositions));

            Assert.False(result.IsValid);
            Assert.Equal(272, result.ItemCapacity);
            Assert.Equal(16, result.NonControllerMemberCount);
            Assert.Equal(16, result.MaximumGraphDistance);
            Assert.Equal(Pos(17, 0, 0), Assert.Single(result.OrphanedKnownMembers));
            Assert.Contains(StorageTopologyIssue.GraphDistanceExceeded, result.Issues);
        }

        [Fact]
        public void MemberLimit_StopsTraversalBeforeCapacityCanExceedLimit()
        {
            FakeTopologySource source = new FakeTopologySource();
            source.Add(Member(Controller, StorageMemberRole.Controller, 10));
            source.Add(Member(Pos(1, 0, 0), StorageMemberRole.CapacityCell, 10));
            source.Add(Member(Pos(2, 0, 0), StorageMemberRole.CapacityCell, 10));
            source.Add(Member(Pos(3, 0, 0), StorageMemberRole.CapacityCell, 10));
            StorageTopologyLimits limits = new StorageTopologyLimits(16, 2);

            StorageStructureSnapshot result = new StorageStructureScanner().Scan(
                source,
                Request(source.MemberPositions, limits));

            Assert.False(result.IsValid);
            Assert.Contains(StorageTopologyIssue.MemberLimitExceeded, result.Issues);
            Assert.Equal(30, result.ItemCapacity);
            Assert.Equal(2, result.NonControllerMemberCount);
            Assert.Equal(3, result.Members.Count);
            Assert.Contains(Pos(3, 0, 0), result.OrphanedKnownMembers);
        }

        [Fact]
        public void UnrelatedMissingChunk_DoesNotBlockIsolatedController()
        {
            FakeTopologySource source = new FakeTopologySource();
            source.Add(Member(Controller, StorageMemberRole.Controller, 256));
            StorageTopologyChunk missing = source.GetChunk(Pos(4, 0, 0));
            source.MissingChunks.Add(missing);

            StorageStructureSnapshot result = new StorageStructureScanner().Scan(
                source,
                Request(source.MemberPositions, new StorageTopologyLimits(4, 10)));

            Assert.True(result.IsComplete);
            Assert.True(result.IsValid);
            Assert.DoesNotContain(missing, result.MissingChunks);
        }

        [Fact]
        public void MissingAdjacentFrontierChunk_FailsClosed()
        {
            FakeTopologySource source = new FakeTopologySource();
            source.Add(Member(Controller, StorageMemberRole.Controller, 256));
            StorageTopologyChunk missing = source.GetChunk(Pos(-1, 0, 0));
            source.MissingChunks.Add(missing);

            StorageStructureSnapshot result = new StorageStructureScanner().Scan(
                source,
                Request(source.MemberPositions));

            Assert.False(result.IsComplete);
            Assert.False(result.IsValid);
            Assert.Contains(missing, result.MissingChunks);
            Assert.Equal(
                new[] { StorageTopologyIssue.RequiredChunkUnavailable },
                result.Issues);
        }

        [Fact]
        public void MissingKnownMemberChunk_StopsBeforeReadingMembers()
        {
            FakeTopologySource source = new FakeTopologySource();
            source.Add(Member(Controller, StorageMemberRole.Controller, 256));
            StorageTopologyPosition known = Pos(4, 0, 0);
            StorageTopologyChunk missing = source.GetChunk(known);
            source.MissingChunks.Add(missing);

            StorageStructureSnapshot result = new StorageStructureScanner().Scan(
                source,
                Request(new[] { Controller, known }));

            Assert.False(result.IsComplete);
            Assert.Equal(0, source.MemberReads);
            Assert.Contains(missing, result.MissingChunks);
        }

        [Fact]
        public void ExcludedMember_SimulatesRemovalAndOrphansDetachedBranch()
        {
            FakeTopologySource source = new FakeTopologySource();
            source.Add(Member(Controller, StorageMemberRole.Controller, 10));
            source.Add(Member(Pos(1, 0, 0), StorageMemberRole.CapacityCell, 10));
            source.Add(Member(Pos(2, 0, 0), StorageMemberRole.CapacityCell, 10));
            source.Add(Member(Pos(3, 0, 0), StorageMemberRole.CapacityCell, 10));
            StorageStructureScanRequest request = new StorageStructureScanRequest(
                Controller,
                WarehouseId,
                new StorageTopologyLimits(),
                source.MemberPositions,
                excludedPosition: Pos(1, 0, 0));

            StorageStructureSnapshot result = new StorageStructureScanner().Scan(source, request);

            Assert.False(result.IsValid);
            Assert.Equal(10, result.ItemCapacity);
            Assert.Single(result.Members);
            Assert.DoesNotContain(Pos(1, 0, 0), result.OrphanedKnownMembers);
            Assert.Equal(
                new[] { Pos(2, 0, 0), Pos(3, 0, 0) },
                result.OrphanedKnownMembers);
        }

        [Fact]
        public void CapacityOverflow_FailsClosedWithoutAddingOverflowingMember()
        {
            FakeTopologySource source = new FakeTopologySource();
            source.Add(Member(Controller, StorageMemberRole.Controller, long.MaxValue));
            source.Add(Member(Pos(1, 0, 0), StorageMemberRole.CapacityCell, 1));

            StorageStructureSnapshot result = new StorageStructureScanner().Scan(
                source,
                Request(source.MemberPositions));

            Assert.False(result.IsValid);
            Assert.Equal(long.MaxValue, result.ItemCapacity);
            Assert.Single(result.Members);
            Assert.Contains(StorageTopologyIssue.CapacityOverflow, result.Issues);
        }

        [Fact]
        public void TypeCapacityOverflow_DoesNotPartiallyApplyMemberCapacity()
        {
            FakeTopologySource source = new FakeTopologySource();
            source.Add(Member(
                Controller,
                StorageMemberRole.Controller,
                10,
                typeCapacity: int.MaxValue));
            source.Add(Member(
                Pos(1, 0, 0),
                StorageMemberRole.CapacityCell,
                20,
                typeCapacity: 1));

            StorageStructureSnapshot result = new StorageStructureScanner().Scan(
                source,
                Request(source.MemberPositions));

            Assert.False(result.IsValid);
            Assert.Equal(10, result.ItemCapacity);
            Assert.Equal(int.MaxValue, result.TypeCapacity);
            Assert.Single(result.Members);
            Assert.Contains(StorageTopologyIssue.TypeCapacityOverflow, result.Issues);
        }

        [Fact]
        public void ReleaseOneLimits_CannotBeRaisedPastSafetyBounds()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new StorageTopologyLimits(17, 256));
            Assert.Throws<ArgumentOutOfRangeException>(() => new StorageTopologyLimits(16, 257));
        }

        private static StorageStructureScanRequest Request(
            IReadOnlyList<StorageTopologyPosition> knownMembers,
            StorageTopologyLimits? limits = null)
        {
            return new StorageStructureScanRequest(
                Controller,
                WarehouseId,
                limits ?? new StorageTopologyLimits(),
                knownMembers);
        }

        private static StorageMemberSnapshot Member(
            StorageTopologyPosition position,
            StorageMemberRole role,
            long capacity,
            string warehouseId = WarehouseId,
            int typeCapacity = 0)
        {
            return new StorageMemberSnapshot(
                position,
                warehouseId,
                role,
                capacity,
                typeCapacity,
                role == StorageMemberRole.Controller
                    ? "vintagekinematics:storagecontroller"
                    : "vintagekinematics:storagecell",
                Controller);
        }

        private static StorageTopologyPosition Pos(int x, int y, int z)
        {
            return new StorageTopologyPosition(x, y, z, 0);
        }

        private sealed class FakeTopologySource : IStorageTopologySource
        {
            private const int ChunkSize = 4;
            private readonly Dictionary<StorageTopologyPosition, StorageMemberSnapshot> members =
                new Dictionary<StorageTopologyPosition, StorageMemberSnapshot>();

            public HashSet<StorageTopologyChunk> MissingChunks { get; } =
                new HashSet<StorageTopologyChunk>();
            public IReadOnlyList<StorageTopologyPosition> MemberPositions =>
                members.Keys.OrderBy(position => position.X).ToArray();
            public int MemberReads { get; private set; }

            public void Add(StorageMemberSnapshot member)
            {
                members.Add(member.Position, member);
            }

            public StorageTopologyChunk GetChunk(StorageTopologyPosition position)
            {
                return new StorageTopologyChunk(
                    FloorDivide(position.X, ChunkSize),
                    FloorDivide(position.InternalY, ChunkSize),
                    FloorDivide(position.Z, ChunkSize),
                    position.Dimension);
            }

            public bool IsChunkLoaded(StorageTopologyChunk chunk)
            {
                return !MissingChunks.Contains(chunk);
            }

            public bool TryGetMember(
                StorageTopologyPosition position,
                out StorageMemberSnapshot member)
            {
                MemberReads++;
                return members.TryGetValue(position, out member);
            }

            private static int FloorDivide(int value, int divisor)
            {
                int quotient = value / divisor;
                int remainder = value % divisor;
                return remainder < 0 ? quotient - 1 : quotient;
            }
        }
    }
}
