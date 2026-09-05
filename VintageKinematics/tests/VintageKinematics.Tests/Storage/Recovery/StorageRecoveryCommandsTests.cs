using VintageKinematics.Storage.Recovery;
using VintageKinematics.Storage.Index;
using VintageKinematics.Storage.Persistence;
using Xunit;

namespace VintageKinematics.Tests.Storage.Recovery
{
    public class StorageRecoveryCommandsTests
    {
        private const string WarehouseId = "00000000-0000-0000-0000-000000000001";
        private static readonly StorageControllerLocation Controller = new(1, 2, 3, 0);

        [Fact]
        public void ConfirmationTokenChangesWithSourceOrEitherRetainedCopy()
        {
            StorageReconciliationResult result = Reconcile(1, 10, 2, 20);
            string controllerToken = StorageRecoveryCommands.CreateConfirmationToken(
                WarehouseId,
                result,
                StorageSnapshotSource.BlockEntity);
            string recoveryToken = StorageRecoveryCommands.CreateConfirmationToken(
                WarehouseId,
                result,
                StorageSnapshotSource.RecoveryRegistry);
            string changedCopyToken = StorageRecoveryCommands.CreateConfirmationToken(
                WarehouseId,
                Reconcile(1, 10, 3, 30),
                StorageSnapshotSource.BlockEntity);

            Assert.NotEqual(controllerToken, recoveryToken);
            Assert.NotEqual(controllerToken, changedCopyToken);
            Assert.Equal(16, controllerToken.Length);
        }

        [Fact]
        public void PlayerFacingCopyDescriptionUsesCountsInsteadOfChecksums()
        {
            var persistence = new KineticStoragePersistence(null);
            var index = new KineticStorageIndex(null, new StorageIndexLimits(256));
            StorageRecoveryRecord record = StorageRecoveryRecord.Create(
                WarehouseId,
                Controller,
                3,
                persistence.Encode(persistence.Capture(index)));

            string description = StorageRecoveryCommands.DescribeCopyForPlayer(
                "World backup",
                StorageSnapshotCopy.FromRecord(record));

            Assert.Equal(
                "World backup: revision 3, 0 item(s) across 0 type(s).",
                description);
            Assert.DoesNotContain("checksum", description);
        }

        [Theory]
        [InlineData("controller")]
        [InlineData("recovery")]
        [InlineData("empty")]
        public void PlayerFacingConfirmationCommandDoesNotRequireCopyingToken(string source)
        {
            string command = StorageRecoveryCommands.ConfirmationCommand(16, source);

            Assert.Equal($"/vk storagerecover 16 {source} confirm", command);
        }

        private static StorageReconciliationResult Reconcile(
            long firstRevision,
            byte firstValue,
            long secondRevision,
            byte secondValue)
        {
            return StorageSnapshotReconciler.Reconcile(
                StorageSnapshotCopy.FromRecord(StorageRecoveryRecord.Create(
                    WarehouseId,
                    Controller,
                    firstRevision,
                    new[] { firstValue })),
                StorageSnapshotCopy.FromRecord(StorageRecoveryRecord.Create(
                    WarehouseId,
                    Controller,
                    secondRevision,
                    new[] { secondValue })));
        }
    }
}
