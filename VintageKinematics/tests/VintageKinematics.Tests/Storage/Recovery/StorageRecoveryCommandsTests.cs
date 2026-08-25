using VintageKinematics.Storage.Recovery;
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
