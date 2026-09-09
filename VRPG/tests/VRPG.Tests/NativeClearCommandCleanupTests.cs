using System;
using HarmonyLib;
using VRPG.Inventory;
using VRPG.Patches;
using Vintagestory.API.Common;
using Vintagestory.Common;
using Xunit;

namespace VRPG.Tests;

[Collection("EngineItemBirth")]
public sealed class NativeClearCommandCleanupTests
{
    [Fact]
    public void SelfClearExceptionReleasesItsResultContext()
    {
        using var scope = new NativeMutationScope("vanilla");
        using var f = new NativeClearCommandFixture();
        f.Online();
        var throwing = new ThrowingDiscard();
        f.Inventories.Add(throwing.InventoryID, throwing);
        Assert.Throws<InvalidOperationException>(() => f.Clear(f.Target));
        AssertReleased();
        f.Inventories.Remove(throwing.InventoryID);
        Assert.Equal(EnumCommandStatus.Success, f.Clear(f.Target).Status);
        AssertReleased();
    }

    [Fact]
    public void NestedRegisteredRefusalDoesNotChangeOuterCommandResult()
    {
        using var scope = new NativeMutationScope("vanilla");
        using var f = new NativeClearCommandFixture();
        var inv = f.Online();
        int nestedChecks = 0;
        NativeInventoryMutationGuardPatch.CustodyResolver = uid => new(uid != "nested" || ++nestedChecks == 1
            ? PlayerItemCustodyOwner.Vanilla : PlayerItemCustodyOwner.Recovery, false);
        f.Target.BeforeBroadcast = () =>
        {
            var nested = f.RegisteredWipe(new[] { "nested" });
            Assert.Equal(EnumCommandStatus.Error, nested.Status);
            Assert.DoesNotContain("nested", f.Server.ClearPlayerInvs);
        };
        Assert.Equal(EnumCommandStatus.Success, f.RegisteredWipe(new[] { "target" }).Status);
        Assert.Equal(0, inv[0]!.StackSize);
        AssertReleased();
    }

    [Fact]
    public void NativeExceptionIsPreservedAndBothCommandContextsAreReleased()
    {
        using var scope = new NativeMutationScope("vanilla");
        using var f = new NativeClearCommandFixture();
        f.Online();
        var expected = new InvalidOperationException("fixture network failure");
        f.Target.BeforeBroadcast = () => throw expected;
        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => f.RegisteredWipe(new[] { "target" })));
        AssertReleased();
        f.Target.BeforeBroadcast = null;
        Assert.Equal(EnumCommandStatus.Success, f.RegisteredWipe(new[] { "target" }).Status);
        AssertReleased();
    }

    private static void AssertReleased()
    {
        Assert.Null(AccessTools.Field(typeof(NativeInventoryMutationGuardPatch), "clearCommandInvocation").GetValue(null));
        Assert.Null(AccessTools.Field(typeof(NativeInventoryMutationGuardPatch), "wipeAttempt").GetValue(null));
    }

    private sealed class ThrowingDiscard() : InventoryPlayerMouseCursor("mouse", "target", null!)
    {
        public override void DiscardAll() => throw new InvalidOperationException("fixture discard callback failure");
    }
}
