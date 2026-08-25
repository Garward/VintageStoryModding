using System;
using VintageKinematics.Storage.Recovery;
using Xunit;

namespace VintageKinematics.Tests.Storage.Recovery
{
    public class KineticStorageRecoverySystemTests
    {
        private const string WarehouseId = "00000000-0000-0000-0000-000000000001";
        private static readonly StorageControllerLocation Controller = new(1, 2, 3, 0);

        [Fact]
        public void MirrorUpsertAndTombstoneRemainEquivalent()
        {
            KineticStorageRecoverySystem system = new KineticStorageRecoverySystem();
            StorageRecoveryRecord live = Record(1, 10);

            system.UpsertMirrors(live);
            system.TombstoneMirrors(WarehouseId, 2);

            Assert.True(system.ControllerRegistry.TryGet(WarehouseId, out StorageRecoveryRecord first));
            Assert.True(system.Registry.TryGet(WarehouseId, out StorageRecoveryRecord second));
            Assert.True(first.IsEquivalentTo(second));
            Assert.True(first.IsTombstone);
        }

        [Fact]
        public void MirrorValidationRejectsBeforeMutatingEitherSide()
        {
            KineticStorageRecoverySystem system = new KineticStorageRecoverySystem();
            StorageRecoveryRecord original = Record(1, 10);
            system.UpsertMirrors(original);
            system.ControllerRegistry.Upsert(Record(2, 20));

            Assert.Throws<InvalidOperationException>(() => system.UpsertMirrors(Record(2, 30)));

            Assert.True(system.Registry.TryGet(WarehouseId, out StorageRecoveryRecord recovery));
            Assert.Equal(1, recovery.Revision);
            Assert.Equal(new byte[] { 10 }, recovery.SnapshotBytes);
        }

        private static StorageRecoveryRecord Record(long revision, byte value)
        {
            return StorageRecoveryRecord.Create(
                WarehouseId,
                Controller,
                revision,
                new[] { value });
        }
    }
}
