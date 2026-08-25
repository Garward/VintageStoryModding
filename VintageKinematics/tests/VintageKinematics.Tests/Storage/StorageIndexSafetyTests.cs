using System;
using VintageKinematics.Api.Storage;
using VintageKinematics.Storage.Index;
using Xunit;

namespace VintageKinematics.Tests.Storage
{
    public class StorageIndexSafetyTests
    {
        [Fact]
        public void Limits_RejectNegativeCapacity()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new StorageIndexLimits(-1));
        }

        [Fact]
        public void InternalEntry_RejectsQuantityOverflow()
        {
            InternalStoredEntry entry = new InternalStoredEntry(
                1,
                new ItemKey(Vintagestory.API.Common.EnumItemClass.Item, "game:stick", 0, 0),
                StorageTestStacks.Create("game:stick"),
                long.MaxValue);

            Assert.Throws<OverflowException>(() => entry.Increase(1));
        }

        [Fact]
        public void StorageStats_ExposeExplicitState()
        {
            StorageStats stats = new StorageStats(
                storedItems: 12,
                itemCapacity: 10,
                entryCount: 1,
                typeCapacity: 0,
                state: StorageState.OverCapacity,
                importRate: 0,
                exportRate: 0);

            Assert.Equal(StorageState.OverCapacity, stats.State);
            Assert.True(stats.IsOverCapacity);
            Assert.False(stats.IsLocked);
            Assert.Equal(0, stats.FreeCapacity);
        }

        [Fact]
        public void StorageStats_ReportPowerSeparatelyFromRecoveryState()
        {
            StorageStats stats = new StorageStats(
                storedItems: 0,
                itemCapacity: 256,
                entryCount: 0,
                typeCapacity: 0,
                state: StorageState.Online,
                importRate: 0,
                exportRate: 0,
                powerRequired: true,
                powered: false);

            Assert.Equal(StorageState.Online, stats.State);
            Assert.False(stats.IsOperational);
            Assert.False(stats.IsLocked);
        }
    }
}
