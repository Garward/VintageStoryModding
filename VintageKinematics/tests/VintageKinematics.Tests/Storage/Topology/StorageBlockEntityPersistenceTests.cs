using Vintagestory.API.Datastructures;
using VintageKinematics.Api.Storage;
using VintageKinematics.BlockEntities.Storage;
using VintageKinematics.Storage.Topology;
using VintageKinematics.Storage.Recovery;
using Xunit;

namespace VintageKinematics.Tests.Storage.Topology
{
    public class StorageBlockEntityPersistenceTests
    {
        private const string WarehouseId = "cbb91396-f8c6-4992-8447-504841a13ed9";

        [Fact]
        public void Controller_LoadsStructureMetadataWithoutClearingLockState()
        {
            TreeAttribute tree = MemberTree();
            tree.SetInt(StorageBlockEntityKeys.StructureState, (int)StorageState.ManualLocked);
            tree.SetLong(StorageBlockEntityKeys.ItemCapacity, 5376);
            tree.SetInt(StorageBlockEntityKeys.TypeCapacity, 4);
            tree.SetLong(StorageBlockEntityKeys.StoredItems, 321);
            tree.SetInt(StorageBlockEntityKeys.StoredEntryCount, 7);
            tree.SetInt(StorageBlockEntityKeys.KnownMemberCount, 2);
            tree[StorageBlockEntityKeys.KnownMemberPrefix + 0] = PositionTree(1, 2, 3, 0);
            tree[StorageBlockEntityKeys.KnownMemberPrefix + 1] = PositionTree(4, 5, 6, 2);
            BEKineticWarehouseTerminal controller = new BEKineticWarehouseTerminal();

            controller.ReadStorageMemberAttributes(tree);
            controller.ReadControllerStorageAttributes(tree);

            Assert.Equal(WarehouseId, controller.WarehouseId);
            Assert.Equal(StorageState.ManualLocked, controller.StructureState);
            Assert.Equal(5376, controller.VerifiedItemCapacity);
            Assert.Equal(4, controller.VerifiedTypeCapacity);
            Assert.Equal(321, controller.SyncedStoredItems);
            Assert.Equal(7, controller.SyncedEntryCount);
            Assert.Equal(
                new StorageTopologyPosition(4, 5, 6, 2),
                controller.KnownMembers[1]);
        }

        [Fact]
        public void InvalidPersistedValues_FailClosed()
        {
            TreeAttribute tree = MemberTree();
            tree.SetString(StorageBlockEntityKeys.WarehouseId, "not-a-uuid");
            tree.SetInt(StorageBlockEntityKeys.StructureState, 999);
            tree.SetLong(StorageBlockEntityKeys.ItemCapacity, -1);
            tree.SetInt(StorageBlockEntityKeys.TypeCapacity, -1);
            BEKineticWarehouseTerminal controller = new BEKineticWarehouseTerminal();

            controller.ReadStorageMemberAttributes(tree);
            controller.ReadControllerStorageAttributes(tree);

            Assert.Null(controller.WarehouseId);
            Assert.Equal(StorageState.StructureUnknown, controller.StructureState);
            Assert.Equal(0, controller.VerifiedItemCapacity);
            Assert.Equal(0, controller.VerifiedTypeCapacity);
        }

        [Fact]
        public void Cell_LoadsOnlyLinkMetadataAndCapacityDefaults()
        {
            BEKineticWarehouseCell cell = new BEKineticWarehouseCell();

            cell.ReadStorageMemberAttributes(MemberTree());

            Assert.Equal(WarehouseId, cell.WarehouseId);
            Assert.Equal(12, cell.ControllerPos.X);
            Assert.Equal(34, cell.ControllerPos.Y);
            Assert.Equal(-56, cell.ControllerPos.Z);
            Assert.Equal(2, cell.ControllerPos.dimension);
            Assert.Equal(1024, cell.CapacityContribution);
            Assert.False(cell.IsController);
        }

        [Fact]
        public void ControllerRetainsMalformedRecoveryEnvelopeVerbatim()
        {
            byte[] malformed = new byte[] { 1, 2, 3, 4 };
            TreeAttribute source = MemberTree();
            source.SetBytes(StorageBlockEntityKeys.RecoveryHeader, malformed);
            BEKineticWarehouseTerminal controller = new BEKineticWarehouseTerminal();
            controller.ReadStorageMemberAttributes(source);
            controller.ReadControllerItemAttributes(source);
            TreeAttribute written = new TreeAttribute();

            controller.WriteControllerItemAttributes(written);

            Assert.Equal(StorageSnapshotCopyState.Invalid, controller.PersistedItemCopy.State);
            Assert.Equal(malformed, written.GetBytes(StorageBlockEntityKeys.RecoveryHeader));
        }

        [Fact]
        public void ControllerRecoveryEnvelopeRoundTripsWithoutMutation()
        {
            StorageRecoveryRecord record = StorageRecoveryRecord.Create(
                WarehouseId,
                new StorageControllerLocation(12, 34, -56, 2),
                7,
                new byte[] { 9, 8, 7 });
            byte[] encoded = StorageRecoveryRegistryCodec.EncodeRecord(record);
            TreeAttribute source = MemberTree();
            source.SetBytes(StorageBlockEntityKeys.RecoveryHeader, encoded);
            BEKineticWarehouseTerminal controller = new BEKineticWarehouseTerminal();
            controller.ReadStorageMemberAttributes(source);

            controller.ReadControllerItemAttributes(source);

            Assert.Equal(StorageSnapshotCopyState.Valid, controller.PersistedItemCopy.State);
            Assert.Equal(7, controller.PersistedItemCopy.Record.Revision);
            Assert.Equal(encoded, controller.PersistedItemCopy.RawBytes);
        }

        [Fact]
        public void ControllerPersistsOnlyCompactSnapshotHeader()
        {
            StorageRecoveryRecord record = StorageRecoveryRecord.Create(
                WarehouseId,
                new StorageControllerLocation(12, 34, -56, 2),
                7,
                new byte[1024]);
            byte[] header = StorageRecoveryRegistryCodec.EncodeIndex(
                new[] { new StorageRecoveryIndexEntry(record) });
            TreeAttribute source = MemberTree();
            source.SetBytes(StorageBlockEntityKeys.RecoveryHeader, header);
            BEKineticWarehouseTerminal controller = new BEKineticWarehouseTerminal();

            controller.ReadControllerItemAttributes(source);

            Assert.NotNull(controller.PersistedItemHeader);
            Assert.Equal(7, controller.PersistedItemHeader.Revision);
            Assert.True(header.Length < record.SnapshotBytes.Length);
            Assert.Equal(StorageSnapshotCopyState.Missing, controller.PersistedItemCopy.State);
        }

        [Fact]
        public void ConfirmedRemoval_DropsPositionFromPersistedKnownSet()
        {
            TreeAttribute tree = MemberTree();
            tree.SetInt(StorageBlockEntityKeys.KnownMemberCount, 2);
            tree[StorageBlockEntityKeys.KnownMemberPrefix + 0] = PositionTree(1, 2, 3, 0);
            tree[StorageBlockEntityKeys.KnownMemberPrefix + 1] = PositionTree(4, 5, 6, 0);
            BEKineticWarehouseTerminal controller = new BEKineticWarehouseTerminal();
            controller.ReadControllerStorageAttributes(tree);

            bool removed = controller.ForgetKnownMember(
                new StorageTopologyPosition(1, 2, 3, 0));

            Assert.True(removed);
            Assert.Equal(
                new StorageTopologyPosition(4, 5, 6, 0),
                Assert.Single(controller.KnownMembers));
        }

        [Fact]
        public void FreshPlacement_DiscardsReusedControllerIdentityAndSummary()
        {
            TreeAttribute tree = MemberTree();
            tree.SetInt(StorageBlockEntityKeys.StructureState, (int)StorageState.RecoveryRequired);
            tree.SetLong(StorageBlockEntityKeys.ItemCapacity, 8448);
            tree.SetLong(StorageBlockEntityKeys.StoredItems, 12);
            tree.SetInt(StorageBlockEntityKeys.KnownMemberCount, 1);
            tree[StorageBlockEntityKeys.KnownMemberPrefix + 0] = PositionTree(1, 2, 3, 0);
            tree.SetBytes(StorageBlockEntityKeys.RecoveryHeader, new byte[] { 1, 2, 3, 4 });
            BEKineticWarehouseTerminal controller = new BEKineticWarehouseTerminal();
            controller.ReadStorageMemberAttributes(tree);
            controller.ReadControllerStorageAttributes(tree);
            controller.ReadControllerItemAttributes(tree);

            controller.ResetForFreshPlacement();

            Assert.Null(controller.WarehouseId);
            Assert.Null(controller.ControllerPos);
            Assert.Equal(StorageState.StructureUnknown, controller.StructureState);
            Assert.Equal(0, controller.VerifiedItemCapacity);
            Assert.Equal(0, controller.SyncedStoredItems);
            Assert.Empty(controller.KnownMembers);
            Assert.Equal(StorageSnapshotCopyState.Missing, controller.PersistedItemCopy.State);
        }

        private static TreeAttribute MemberTree()
        {
            TreeAttribute tree = new TreeAttribute();
            tree.SetString(StorageBlockEntityKeys.WarehouseId, WarehouseId);
            tree.SetInt(StorageBlockEntityKeys.ControllerX, 12);
            tree.SetInt(StorageBlockEntityKeys.ControllerY, 34);
            tree.SetInt(StorageBlockEntityKeys.ControllerZ, -56);
            tree.SetInt(StorageBlockEntityKeys.ControllerDimension, 2);
            return tree;
        }

        private static TreeAttribute PositionTree(int x, int y, int z, int dimension)
        {
            TreeAttribute tree = new TreeAttribute();
            tree.SetInt("x", x);
            tree.SetInt("y", y);
            tree.SetInt("z", z);
            tree.SetInt("dimension", dimension);
            return tree;
        }
    }
}
