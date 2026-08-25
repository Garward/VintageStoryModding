using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using VintageKinematics.Storage.Index;
using VintageKinematics.Storage.Persistence;
using VintageKinematics.Storage.Recovery;
using Xunit;

namespace VintageKinematics.Tests.Storage.Recovery
{
    public class StorageOrphanRecoveryPolicyTests
    {
        private const string WarehouseId = "00000000-0000-0000-0000-000000000001";
        private static readonly StorageControllerLocation Controller = new(1, 2, 3, 0);
        private readonly KineticStoragePersistence persistence = new(null);

        [Fact]
        public void AgreeingEmptyMirrorsCanBeRetiredAutomatically()
        {
            StorageRecoveryRecord record = Record(EmptySnapshot());

            Assert.True(StorageOrphanRecoveryPolicy.CanTombstoneEmptyMirrors(
                record,
                record,
                persistence));
        }

        [Fact]
        public void NonemptyMirrorCannotBeRetiredAutomatically()
        {
            PersistedStorageEntry entry = StorageEntryCodec.Create(
                1,
                EnumItemClass.Item,
                "game:gear-rusty",
                StorageAttributeCodec.Encode(new TreeAttribute()),
                1);
            byte[] snapshot = persistence.Encode(new StoragePersistenceSnapshot(
                2,
                new[] { entry }));
            StorageRecoveryRecord record = Record(snapshot);

            Assert.False(StorageOrphanRecoveryPolicy.CanTombstoneEmptyMirrors(
                record,
                record,
                persistence));
        }

        [Fact]
        public void DivergentMirrorsCannotBeRetiredAutomatically()
        {
            StorageRecoveryRecord first = Record(EmptySnapshot(), revision: 1);
            StorageRecoveryRecord second = Record(EmptySnapshot(), revision: 2);

            Assert.False(StorageOrphanRecoveryPolicy.CanTombstoneEmptyMirrors(
                first,
                second,
                persistence));
        }

        private byte[] EmptySnapshot()
        {
            var index = new KineticStorageIndex(null, new StorageIndexLimits(4096));
            return persistence.Encode(persistence.Capture(index));
        }

        private static StorageRecoveryRecord Record(byte[] snapshot, long revision = 1)
        {
            return StorageRecoveryRecord.Create(
                WarehouseId,
                Controller,
                revision,
                snapshot);
        }
    }
}
