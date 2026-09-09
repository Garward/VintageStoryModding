using System;
using System.Linq;
using HarmonyLib;
using VRPG.Inventory;
using VRPG.Patches;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Xunit;

namespace VRPG.Tests;

[Collection("EngineItemBirth")]
public sealed class NativeClearCommandTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void SelfClearCannotReportSuccessAfterManagerOrInventoryAuthorityLoss(int refuseAt)
    {
        using var scope = new NativeMutationScope("vanilla");
        using var f = new NativeClearCommandFixture();
        var inv = f.Online();
        int queries = 0;
        NativeInventoryMutationGuardPatch.CustodyResolver = _ => new(++queries < refuseAt
            ? PlayerItemCustodyOwner.Vanilla : PlayerItemCustodyOwner.Recovery, false);
        var result = f.Clear(f.Target);
        Assert.Equal(EnumCommandStatus.Error, result.Status);
        Assert.Equal("vrpg:inventory-clear-interrupted", result.StatusMessage);
        Assert.Equal(7, inv[0]!.StackSize);
    }

    [Fact]
    public void AuthorityLossAtActualClearCannotBecomeCommandSuccess()
    {
        using var scope = new NativeMutationScope("vanilla");
        using var f = new NativeClearCommandFixture();
        var inv = f.Online();
        int queries = 0;
        NativeInventoryMutationGuardPatch.CustodyResolver = _ => new(++queries < 3
            ? PlayerItemCustodyOwner.Vanilla : PlayerItemCustodyOwner.Recovery, false);
        var result = f.RegisteredWipe(new[] { "target" });
        Assert.Equal(EnumCommandStatus.Error, result.Status);
        Assert.Equal("vrpg:inventory-clear-interrupted", result.StatusMessage);
        Assert.Equal(7, inv[0]!.StackSize);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(11)]
    public void LateAuthorityLossSurvivesBothNativeAggregationBranches(int targets)
    {
        using var scope = new NativeMutationScope("vanilla");
        using var f = new NativeClearCommandFixture();
        int queries = 0;
        NativeInventoryMutationGuardPatch.CustodyResolver = _ => ++queries <= targets
            ? new(PlayerItemCustodyOwner.Vanilla, false) : throw new InvalidOperationException("authority disappeared");
        var result = f.RegisteredWipe(Enumerable.Range(0, targets).Select(i => "target" + i).ToArray());
        Assert.Equal(EnumCommandStatus.Error, result.Status);
        Assert.Equal("vrpg:inventory-clear-interrupted", result.StatusMessage);
        Assert.Empty(f.Server.ClearPlayerInvs);
        scope.Resolve("vanilla");
        Assert.Equal(EnumCommandStatus.Success, f.RegisteredWipe(new[] { "target" }).Status);
        Assert.Contains("target", f.Server.ClearPlayerInvs);
    }

    [Fact]
    public void EmptySelectionPreservesNativeErrorWithoutInventedMutation()
    {
        using var scope = new NativeMutationScope("canonical");
        using var f = new NativeClearCommandFixture();
        var result = f.RegisteredWipe(Array.Empty<string>());
        Assert.Equal(EnumCommandStatus.Error, result.Status);
        Assert.Equal("No players found that match your selector", result.StatusMessage);
        Assert.Empty(f.Server.ClearPlayerInvs);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(11)]
    public void RegisteredPlayerClearRefusesProtectedSelectionWithoutPartialWipe(int count)
    {
        using var scope = new NativeMutationScope("vanilla");
        using var f = new NativeClearCommandFixture();
        NativeInventoryMutationGuardPatch.CustodyResolver = uid => new(uid == "protected"
            ? PlayerItemCustodyOwner.Canonical : PlayerItemCustodyOwner.Vanilla, false);
        var inv = f.Online();
        var selected = Enumerable.Range(0, count).Select(i => i == 0 ? "target" : "offline" + i).ToArray();
        selected[^1] = "protected";
        var result = f.RegisteredWipe(selected);
        Assert.Equal(EnumCommandStatus.Error, result.Status);
        Assert.Equal("vrpg:inventory-clear-refused", result.StatusMessage);
        Assert.Equal(7, inv[0]!.StackSize);
        Assert.Empty(f.Server.ClearPlayerInvs);
        Assert.Equal(0, f.Target.Broadcasts);
        Assert.Equal(Privilege.controlserver, AccessTools.Field(typeof(ChatCommandImpl), "privilege")
            .GetValue(f.Api.ChatCommands.Get("player")["clearinv"]));
    }

    [Fact]
    public void RegisteredPlayerClearPreservesVanillaTargetDespiteProtectedCaller()
    {
        using var scope = new NativeMutationScope("canonical");
        using var f = new NativeClearCommandFixture();
        NativeInventoryMutationGuardPatch.CustodyResolver = uid => new(uid == "target"
            ? PlayerItemCustodyOwner.Vanilla : PlayerItemCustodyOwner.Canonical, false);
        var inv = f.Online();
        var caller = new NativeMutationFixture().Player;
        Assert.Equal(EnumCommandStatus.Success, f.RegisteredWipe(new[] { "target" }, caller).Status);
        Assert.Equal(0, inv[0]!.StackSize);
        Assert.Equal(1, f.Target.Broadcasts);
    }

    [Fact]
    public void AllVanillaLargeSelectionPreservesNativeQueueAndSuccess()
    {
        using var scope = new NativeMutationScope("vanilla");
        using var f = new NativeClearCommandFixture();
        var selected = Enumerable.Range(0, 11).Select(i => "offline" + i).ToArray();
        Assert.Equal(EnumCommandStatus.Success, f.RegisteredWipe(selected).Status);
        Assert.Equal(11, f.Server.ClearPlayerInvs.Count);
    }

    [Fact]
    public void LateRefusalReportsIncompleteCommandWithoutClaimingPriorClearWasRolledBack()
    {
        using var scope = new NativeMutationScope("vanilla");
        using var f = new NativeClearCommandFixture();
        var inv = f.Online();
        int otherQueries = 0;
        NativeInventoryMutationGuardPatch.CustodyResolver = uid => new(uid != "other" || ++otherQueries == 1
            ? PlayerItemCustodyOwner.Vanilla : PlayerItemCustodyOwner.Recovery, false);
        var result = f.RegisteredWipe(new[] { "target", "other" });
        Assert.Equal(EnumCommandStatus.Error, result.Status);
        Assert.Equal("vrpg:inventory-clear-interrupted", result.StatusMessage);
        Assert.Equal(0, inv[0]!.StackSize);
        Assert.DoesNotContain("other", f.Server.ClearPlayerInvs);
    }

    [Theory]
    [InlineData("canonical", false)]
    [InlineData("canonical", true)]
    [InlineData("recovery", false)]
    [InlineData("recovery", true)]
    [InlineData("vanilla", false)]
    [InlineData("vanilla", true)]
    [InlineData("absent", false)]
    [InlineData("throw", false)]
    public void WipeUsesSelectedTargetBeforeAnyEffect(string custody, bool online)
    {
        using var scope = new NativeMutationScope(custody);
        using var f = new NativeClearCommandFixture();
        var caller = new NativeMutationFixture().Player;
        var resolver = NativeInventoryMutationGuardPatch.CustodyResolver;
        if (resolver != null) NativeInventoryMutationGuardPatch.CustodyResolver = uid => uid == "player"
            ? new(PlayerItemCustodyOwner.Vanilla, false) : resolver(uid);
        var inv = online ? f.Online() : null;
        var stack = inv?[0]!.Itemstack;
        var result = f.Wipe(caller: caller);
        bool allowed = custody == "vanilla";
        Assert.Equal(allowed ? EnumCommandStatus.Success : EnumCommandStatus.Error, result.Status);
        Assert.Equal(allowed && online ? 1 : 0, f.Target.Broadcasts);
        Assert.Equal(allowed && !online, f.Server.ClearPlayerInvs.Contains("target"));
        if (online) Assert.Same(allowed ? null : stack, inv![0]!.Itemstack);
    }

    [Theory]
    [InlineData("canonical")]
    [InlineData("recovery")]
    [InlineData("vanilla")]
    [InlineData("absent")]
    [InlineData("throw")]
    [InlineData("unknown")]
    public void RegisteredClearCallbackReturnsTruthfulResult(string custody)
    {
        using var scope = new NativeMutationScope(custody);
        using var f = new NativeClearCommandFixture();
        var inv = f.Online();
        var stack = inv[0]!.Itemstack;
        var result = f.Clear(custody == "unknown" ? null : f.Target);
        Assert.Equal(custody == "vanilla" ? EnumCommandStatus.Success : EnumCommandStatus.Error, result.Status);
        Assert.Same(custody == "vanilla" ? null : stack, inv[0]!.Itemstack);
        var command = f.Api.ChatCommands.Get("clearinv");
        Assert.Equal(Privilege.gamemode, AccessTools.Field(typeof(ChatCommandImpl), "privilege").GetValue(command));
        if (custody != "vanilla") Assert.Equal("vrpg:inventory-clear-refused", result.StatusMessage);
    }

    [Theory]
    [InlineData("canonical", true)]
    [InlineData("recovery", true)]
    [InlineData("vanilla", true)]
    [InlineData("absent", true)]
    [InlineData("throw", true)]
    [InlineData("vanilla", false)]
    [InlineData("canonical", false)]
    public void NativeInitRechecksQueuedRequestBeforeClearAndStillReconstructs(string custody, bool queued)
    {
        using var scope = new NativeMutationScope("vanilla");
        using var f = new NativeClearCommandFixture();
        f.Serialize();
        if (queued) Assert.Equal(EnumCommandStatus.Success, f.Wipe("TARGET").Status);
        f.Server.ClearPlayerInvs.Add("other");
        scope.Resolve(custody);
        Assert.Null(f.Server.PlayerByUid("target"));
        f.Data.Init(f.Server);
        var inv = Assert.IsType<InventoryPlayerHotbar>(f.Inventories["hotbar-target"]);
        Assert.Equal(queued && custody == "vanilla" ? 0 : 7, inv[0]!.StackSize);
        Assert.DoesNotContain("target", f.Server.ClearPlayerInvs);
        Assert.Contains("other", f.Server.ClearPlayerInvs);
        Assert.True(f.Data.PickingRange > 0);
        Assert.NotNull(f.Data.EntityPlayer);
        if (custody == "vanilla" && queued)
        {
            // A finished Init cannot lend reconstruction authority to later ownerless calls.
            inv[0]!.Itemstack = new ItemStack(f.Item, 9);
            inv.Clear();
            Assert.Equal(9, inv[0]!.StackSize);
        }
    }
}
