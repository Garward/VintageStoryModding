using System;
using System.Collections.Generic;
using HarmonyLib;
using VRPG.Inventory;
using VRPG.Patches;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.Common;
using Vintagestory.Server;
using Xunit;

namespace VRPG.Tests;

[Collection("EngineItemBirth")]
public sealed class NativeClearReconstructionTests
{
    [Fact]
    public void QueuedClearRechecksCustodyAfterNativeDeserialization()
    {
        using var scope = new NativeMutationScope("vanilla");
        using var f = new NativeClearCommandFixture();
        f.Serialize();
        Assert.Equal(EnumCommandStatus.Success, f.Wipe().Status);
        WithProbe(() =>
        {
            ProbeInventory.AfterRead = _ => scope.Resolve("canonical");
            f.Data.Init(f.Server);
            Assert.Equal(7, f.Inventories["hotbar-target"][0].StackSize);
            Assert.Empty(f.Server.ClearPlayerInvs);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReconstructionCannotLendEvidenceToRepeatedOrOtherInventoryClear(bool throwDuringClear)
    {
        using var scope = new NativeMutationScope("vanilla");
        using var f = new NativeClearCommandFixture();
        f.Serialize();
        Assert.Equal(EnumCommandStatus.Success, f.Wipe().Status);
        var other = new InventoryPlayerHotbar("hotbar-other", f.Api);
        other[0]!.Itemstack = new ItemStack(f.Item, 11);
        int observed = 0;
        WithProbe(() =>
        {
            ProbeInventory.BeforeEnumerate = inv =>
            {
                ProbeInventory.BeforeEnumerate = null;
                inv.Clear();
                other.Clear();
                observed = inv[0]!.StackSize;
                if (throwDuringClear) throw new InvalidOperationException("fixture enumerator failed before first slot");
            };
            f.Data.Init(f.Server);
            Assert.Equal(7, observed);
            Assert.Equal(11, other[0]!.StackSize);
            var loaded = f.Inventories["hotbar-target"];
            Assert.Equal(throwDuringClear ? 7 : 0, loaded[0].StackSize);
            loaded[0].Itemstack = new ItemStack(f.Item, 13);
            loaded.Clear();
            Assert.Equal(13, loaded[0].StackSize);
            Assert.Empty(f.Server.ClearPlayerInvs);
            Assert.Equal(throwDuringClear ? 2 : 0, f.Logger.Errors);
        });
    }

    [Fact]
    public void MismatchedSerializedOwnerDoesNotBorrowLoadedTargetsVanillaCustody()
    {
        using var scope = new NativeMutationScope("vanilla");
        using var f = new NativeClearCommandFixture();
        f.Serialize("other");
        Assert.Equal(EnumCommandStatus.Success, f.Wipe().Status);
        f.Data.Init(f.Server);
        Assert.Equal(7, f.Inventories["hotbar-other"][0].StackSize);
        Assert.Empty(f.Server.ClearPlayerInvs);
    }

    [Fact]
    public void ProtectedQueueIsRetiredBeforeReconstructionCanChangeCustodyBack()
    {
        using var scope = new NativeMutationScope("vanilla");
        using var f = new NativeClearCommandFixture();
        f.Serialize();
        Assert.Equal(EnumCommandStatus.Success, f.Wipe().Status);
        scope.Resolve("canonical");
        WithProbe(() =>
        {
            ProbeInventory.AfterRead = _ => scope.Resolve("vanilla");
            f.Data.Init(f.Server);
            Assert.Equal(7, f.Inventories["hotbar-target"][0].StackSize);
            Assert.Empty(f.Server.ClearPlayerInvs);
        });
    }

    private static void WithProbe(Action action)
    {
        ServerMain.ClassRegistry.RegisterInventoryClass("hotbar", typeof(ProbeInventory));
        try { action(); }
        finally { ProbeInventory.AfterRead = null; ProbeInventory.BeforeEnumerate = null; }
    }

    public sealed class ProbeInventory(string id, ICoreAPI api) : InventoryPlayerHotbar(id, api)
    {
        internal static Action<ProbeInventory>? AfterRead;
        internal static Action<ProbeInventory>? BeforeEnumerate;
        public override void FromTreeAttributes(ITreeAttribute tree)
        { base.FromTreeAttributes(tree); AfterRead?.Invoke(this); }
        public override IEnumerator<ItemSlot> GetEnumerator()
        { BeforeEnumerate?.Invoke(this); return base.GetEnumerator(); }
    }
}
