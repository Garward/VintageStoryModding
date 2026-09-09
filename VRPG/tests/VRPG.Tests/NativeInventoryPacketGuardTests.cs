using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using HarmonyLib;
using VRPG.Inventory;
using VRPG.Patches;
using Vintagestory.API.Common;
using Vintagestory.Server;
using Xunit;
using Xunit.Abstractions;

namespace VRPG.Tests;

[Collection("EngineItemBirth")]
public sealed class NativeInventoryPacketGuardTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("HandleSelectedHotbarSlot")]
    [InlineData("HandleActivateInventorySlot")]
    [InlineData("HandleCreateItemstack")]
    [InlineData("HandleMoveItemstack")]
    [InlineData("HandleFlipItemStacks")]
    [InlineData("HandleHandInteraction")]
    [InlineData("HandleToolMode")]
    [InlineData("HandleInvOpenClose")]
    [InlineData("secureMouseSlotContents")]
    public void UnknownIdentityRefusesBeforeMalformedPacketOrNativeAccess(string name)
    {
        using var scope = new GuardScope();
        var method = AccessTools.Method(scope.NativeType, name);
        output.WriteLine(name + " " + method.Module.ModuleVersionId + " "
            + Convert.ToHexString(SHA256.HashData(method.GetMethodBody()!.GetILAsByteArray()!)));
        Assert.Null(Record.Exception(() => method.Invoke(scope.Native,
            name == "secureMouseSlotContents" ? new object?[] { null } : new object?[] { null, null })));
    }

    public static System.Collections.Generic.IEnumerable<object[]> NativeCases()
    {
        foreach (string method in new[] { "HandleSelectedHotbarSlot", "HandleActivateInventorySlot", "HandleCreateItemstack",
            "HandleMoveItemstack", "HandleFlipItemStacks", "HandleHandInteraction", "HandleToolMode", "HandleInvOpenClose", "secureMouseSlotContents" })
            foreach (var owner in new[] { PlayerItemCustodyOwner.Canonical, PlayerItemCustodyOwner.Recovery, PlayerItemCustodyOwner.Vanilla })
                yield return new object[] { method, owner };
    }

    [Theory]
    [MemberData(nameof(NativeCases))]
    public void NativeHandlersPreserveCompetingCustodyAndKeepVanillaCallbacks(string name, PlayerItemCustodyOwner owner)
    {
        using var scope = new GuardScope();
        var fixture = new NativeInventoryGuardFixture(scope.Native);
        string? resolvedUid = null;
        string? recoveryUid = null;
        NativeInventoryPacketGuardPatch.CustodyResolver = uid => { resolvedUid = uid; return new(owner, owner == PlayerItemCustodyOwner.Canonical); };
        NativeInventoryPacketGuardPatch.CursorRecoveryObserver = uid => recoveryUid = uid;
        var error = Record.Exception(() => AccessTools.Method(scope.NativeType, name).Invoke(scope.Native,
            name == "secureMouseSlotContents" ? new object[] { fixture.Player } : new object[] { fixture.Packet(name), fixture.Client }));
        if (owner != PlayerItemCustodyOwner.Vanilla)
        {
            Assert.Null(error);
            Assert.Equal(7, fixture.Inventory[0].StackSize);
            Assert.Null(fixture.Inventory[1].Itemstack);
            Assert.Equal(7, fixture.Hotbar[0].StackSize);
            Assert.Equal(5, fixture.Cursor[0].StackSize);
            Assert.Equal(0, fixture.Events.SelectionCalls + fixture.Item.ToolCalls + fixture.Item.UseCalls + fixture.Inventory.OpenCalls + fixture.Inventory.ModifiedCalls + fixture.Cursor.Drops);
            Assert.Equal(name == "secureMouseSlotContents" ? "player" : null, recoveryUid);
            Assert.Equal("player", resolvedUid); // Never trusts ConnectedClient.SentPlayerUid or packet inventory IDs.
            return;
        }
        Assert.Null(recoveryUid);
        switch (name)
        {
            case "HandleSelectedHotbarSlot":
                Assert.IsType<NativeInventoryGuardFixture.SelectionCallbackReached>(Assert.IsType<TargetInvocationException>(error).InnerException);
                Assert.Equal(1, fixture.Events.SelectionCalls);
                break;
            case "HandleCreateItemstack":
                // Headless fixture ends at the native static logger after assignment/dirty callback.
                Assert.Equal(4, fixture.Inventory[1].StackSize);
                Assert.Equal(1, fixture.Inventory.ModifiedCalls);
                break;
            case "HandleActivateInventorySlot":
            case "HandleMoveItemstack":
            case "HandleFlipItemStacks":
                Assert.Null(error);
                Assert.Null(fixture.Inventory[0].Itemstack);
                Assert.Equal(7, fixture.Inventory[1].StackSize);
                break;
            case "HandleToolMode": Assert.Null(error); Assert.Equal(1, fixture.Item.ToolCalls); break;
            case "HandleHandInteraction": Assert.Null(error); Assert.Equal(1, fixture.Item.UseCalls); break;
            case "HandleInvOpenClose": Assert.Null(error); Assert.Equal(1, fixture.Inventory.OpenCalls); break;
            case "secureMouseSlotContents": Assert.Null(error); Assert.Null(fixture.Cursor[0].Itemstack); Assert.Equal(1, fixture.Cursor.Drops); break;
        }
        Assert.Equal("player", resolvedUid);
    }

    [Theory]
    [InlineData("OnPlayerDisconnect", PlayerItemCustodyOwner.Canonical)]
    [InlineData("OnPlayerDisconnect", PlayerItemCustodyOwner.Recovery)]
    [InlineData("OnPlayerDisconnect", PlayerItemCustodyOwner.Vanilla)]
    [InlineData("OnBeginShutdown", PlayerItemCustodyOwner.Canonical)]
    [InlineData("OnBeginShutdown", PlayerItemCustodyOwner.Recovery)]
    [InlineData("OnBeginShutdown", PlayerItemCustodyOwner.Vanilla)]
    public void NativeLifecycleRetainsUnresolvedCursorWithoutClaimingDiskDurability(string name, PlayerItemCustodyOwner owner)
    {
        using var scope = new GuardScope();
        var fixture = new NativeInventoryGuardFixture(scope.Native);
        fixture.Server.Clients = new Vintagestory.API.Datastructures.CachingConcurrentDictionary<int, ConnectedClient>();
        fixture.Server.Clients[1] = fixture.Client;
        NativeInventoryPacketGuardPatch.CustodyResolver = _ => new(owner, false);
        bool recovery = false;
        NativeInventoryPacketGuardPatch.CursorRecoveryObserver = _ => recovery = true;
        Assert.Null(Record.Exception(() => AccessTools.Method(scope.NativeType, name).Invoke(scope.Native,
            name == "OnPlayerDisconnect" ? new object[] { fixture.Player } : Array.Empty<object>())));
        Assert.Equal(owner == PlayerItemCustodyOwner.Vanilla ? 0 : 5, fixture.Cursor[0].StackSize);
        Assert.Equal(owner != PlayerItemCustodyOwner.Vanilla, recovery);
        Assert.Same(fixture.Cursor, fixture.Player.InventoryManager.GetOwnInventory("mouse"));
        var serialized = new Vintagestory.API.Datastructures.TreeAttribute();
        fixture.Cursor.ToTreeAttributes(serialized);
        Assert.Empty(serialized); // Native mouse inventory does not serialize its slot.
    }

    [Theory]
    [InlineData("unpatch")]
    [InlineData("conflict")]
    [InlineData("resolver")]
    [InlineData("observer")]
    [InlineData("reset")]
    public void ReadinessIsWithdrawnOnLostBoundaryOrAuthority(string failure)
    {
        using var scope = new GuardScope();
        NativeInventoryPacketGuardPatch.CustodyResolver = _ => PlayerItemCustodySnapshot.Unresolved;
        NativeInventoryPacketGuardPatch.CursorRecoveryObserver = _ => { };
        Assert.True(NativeInventoryPacketGuardPatch.IsReady);
        var target = AccessTools.Method(scope.NativeType, "HandleMoveItemstack");
        if (failure == "unpatch") scope.Harmony.Unpatch(target, HarmonyPatchType.Prefix, scope.Harmony.Id);
        if (failure == "conflict") scope.Harmony.Patch(target, postfix: new HarmonyMethod(typeof(NativeInventoryPacketGuardTests), nameof(ConflictPostfix)));
        if (failure == "resolver") NativeInventoryPacketGuardPatch.CustodyResolver = null;
        if (failure == "observer") NativeInventoryPacketGuardPatch.CursorRecoveryObserver = null;
        if (failure == "reset") NativeInventoryPacketGuardPatch.Reset();
        Assert.False(NativeInventoryPacketGuardPatch.IsReady);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-body")]
    [InlineData("conflict")]
    public void UnsupportedBoundaryCannotLeavePartialInstallation(string failure)
    {
        using var scope = new GuardScope();
        NativeInventoryPacketGuardPatch.Reset();
        var target = AccessTools.Method(scope.NativeType, "HandleToolMode");
        if (failure == "conflict") scope.Harmony.Patch(target, postfix: new HarmonyMethod(typeof(NativeInventoryPacketGuardTests), nameof(ConflictPostfix)));
        NativeInventoryPacketGuardPatch.Patch(scope.Harmony, PlayerDefenseServiceTests.Proxy.Create<ILogger>((_, _) => null),
            (type, name, args) => name == "HandleToolMode" && failure != "conflict"
                ? failure == "missing" ? null : AccessTools.Method(type, "HandleMoveItemstack", args)
                : AccessTools.Method(type, name, args));
        Assert.False(NativeInventoryPacketGuardPatch.IsInstalled);
        Assert.DoesNotContain(Harmony.GetAllPatchedMethods(), method => Harmony.GetPatchInfo(method)?.Prefixes.Any(
            p => p.PatchMethod.DeclaringType == typeof(NativeInventoryPacketGuardPatch)) == true);
    }

    [Fact]
    public void ThrowingResolverRefusesAndReinstallationCannotKeepOldAuthority()
    {
        using var scope = new GuardScope();
        var fixture = new NativeInventoryGuardFixture(scope.Native);
        NativeInventoryPacketGuardPatch.CustodyResolver = _ => throw new InvalidOperationException("unavailable");
        Assert.False(NativeInventoryPacketGuardPatch.PacketPrefix(fixture.Client));
        NativeInventoryPacketGuardPatch.CustodyResolver = _ => new(PlayerItemCustodyOwner.Vanilla, false);
        Assert.True(NativeInventoryPacketGuardPatch.PacketPrefix(fixture.Client));
        NativeInventoryPacketGuardPatch.Reset();
        NativeInventoryPacketGuardPatch.Patch(scope.Harmony, PlayerDefenseServiceTests.Proxy.Create<ILogger>((_, _) => null));
        Assert.False(NativeInventoryPacketGuardPatch.PacketPrefix(fixture.Client));
        Assert.False(NativeInventoryPacketGuardPatch.IsReady);
    }

    private static void ConflictPostfix() { }

    [Theory]
    [InlineData(".ctor")]
    [InlineData("OnPlayerDisconnect")]
    [InlineData("OnBeginShutdown")]
    public void RoutingOrLifecycleCallerPatchesWithdrawRefusalReadiness(string name)
    {
        using var scope = new GuardScope();
        NativeInventoryPacketGuardPatch.CustodyResolver = _ => PlayerItemCustodySnapshot.Unresolved;
        NativeInventoryPacketGuardPatch.CursorRecoveryObserver = _ => { };
        MethodBase target = name == ".ctor" ? AccessTools.Constructor(scope.NativeType, new[] { typeof(ServerMain) }) : AccessTools.Method(scope.NativeType, name);
        output.WriteLine(name + " " + Convert.ToHexString(SHA256.HashData(target.GetMethodBody()!.GetILAsByteArray()!)));
        Assert.True(NativeInventoryPacketGuardPatch.IsReady);
        scope.Harmony.Patch(target, postfix: new HarmonyMethod(typeof(NativeInventoryPacketGuardTests), nameof(ConflictPostfix)));
        Assert.False(NativeInventoryPacketGuardPatch.IsReady);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CursorObserverFailureCannotLaterReopenReadinessWithUnrecordedSource(bool throws)
    {
        using var scope = new GuardScope();
        var fixture = new NativeInventoryGuardFixture(scope.Native);
        NativeInventoryPacketGuardPatch.CustodyResolver = _ => PlayerItemCustodySnapshot.Unresolved;
        NativeInventoryPacketGuardPatch.CursorRecoveryObserver = throws ? _ => throw new InvalidOperationException("observer failed") : null;
        Assert.False(NativeInventoryPacketGuardPatch.CursorPrefix(fixture.Player));
        Assert.Equal(5, fixture.Cursor[0].StackSize);
        NativeInventoryPacketGuardPatch.CursorRecoveryObserver = _ => { };
        Assert.False(NativeInventoryPacketGuardPatch.IsReady);
    }

    [Fact]
    public void EmptyCursorNeedsNoRecoveryAndMissingConnectedPlayerNeverUsesClaimedUid()
    {
        using var scope = new GuardScope();
        var fixture = new NativeInventoryGuardFixture(scope.Native);
        NativeInventoryPacketGuardPatch.CustodyResolver = _ => PlayerItemCustodySnapshot.Unresolved;
        NativeInventoryPacketGuardPatch.CursorRecoveryObserver = _ => throw new InvalidOperationException("empty cursor should not be reported");
        fixture.Cursor[0].Itemstack = null;
        Assert.False(NativeInventoryPacketGuardPatch.CursorPrefix(fixture.Player));
        Assert.True(NativeInventoryPacketGuardPatch.IsReady);
        NativeInventoryPacketGuardPatch.CustodyResolver = _ => new(PlayerItemCustodyOwner.Vanilla, false);
        Assert.False(NativeInventoryPacketGuardPatch.PacketPrefix(new ConnectedClient(1) { SentPlayerUid = "player" }));
    }

    private sealed class GuardScope : IDisposable
    {
        public readonly Harmony Harmony = new("vrpg.tests.native-guard." + Guid.NewGuid());
        public readonly Type NativeType = AccessTools.TypeByName("Vintagestory.Server.ServerSystemInventory");
        public readonly object Native;
        public GuardScope()
        {
            Native = RuntimeHelpers.GetUninitializedObject(NativeType);
            NativeInventoryPacketGuardPatch.Patch(Harmony, PlayerDefenseServiceTests.Proxy.Create<ILogger>((_, _) => null));
        }
        public void Dispose()
        {
            Harmony.UnpatchAll(Harmony.Id);
            NativeInventoryPacketGuardPatch.Reset();
        }
    }
}
