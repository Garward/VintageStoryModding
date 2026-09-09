using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using VRPG.Inventory;
using VRPG.Patches;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.Common;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;
using Vintagestory.Server;
using Xunit;
using Xunit.Abstractions;

namespace VRPG.Tests;

[Collection("EngineItemBirth")]
public sealed class NativeInventoryMutationGuardTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("canonical")]
    [InlineData("recovery")]
    [InlineData("absent")]
    [InlineData("throw")]
    [InlineData("unknown")]
    [InlineData("vanilla")]
    public void ActiveFullDropRefusalPreservesHandControlsBeforeNativeCancel(string custody)
    {
        using var scope = new NativeMutationScope(custody);
        var f = new NativeMutationFixture();
        var hotbar = new NativeInventoryGuardFixture.ProbeInventory("hotbar-player") { Api = f.Api };
        f.Manager.Inventories["hotbar-player"] = hotbar;
        var stack = hotbar[0].Itemstack = new(f.Item, 7);
        if (custody == "unknown") f.Manager.player = null!;
        f.Entity.Controls.HandUse = EnumHandInteract.HeldItemInteract;
        f.Entity.Controls.LeftMouseDown = true;
        f.Entity.Controls.UsingCount = 9;
        Assert.Equal(custody == "vanilla", f.Manager.DropItem(hotbar[0], true));
        bool allowed = custody == "vanilla";
        Assert.Equal(allowed ? 1 : 0, f.Item.DropCalls);
        Assert.Equal(allowed ? 1 : 0, f.Item.CancelCalls);
        Assert.Equal(allowed ? 1 : 0, f.Item.StopCalls);
        Assert.Equal(allowed ? EnumHandInteract.None : EnumHandInteract.HeldItemInteract, f.Entity.Controls.HandUse);
        Assert.Equal(9, f.Entity.Controls.UsingCount);
        Assert.True(f.Entity.Controls.LeftMouseDown);
        Assert.Equal(allowed ? 7 : 0, f.Ground[0].StackSize);
        if (!allowed)
        {
            Assert.Same(stack, hotbar[0].Itemstack);
            Assert.Equal(7, stack.StackSize);
            Assert.Equal(0, hotbar.ModifiedCalls);
            Assert.Empty(hotbar.DirtySlots);
        }
    }
    [Theory]
    [InlineData("canonical", false)]
    [InlineData("recovery", false)]
    [InlineData("absent", false)]
    [InlineData("throw", false)]
    [InlineData("vanilla", true)]
    public void ActualCmdGiveReturnsNativeErrorWhenManagerRefuses(string custody, bool accepted)
    {
        using var scope = new NativeMutationScope(custody);
        var f = new NativeMutationFixture();
        var args = new TextCommandCallingArgs();
        foreach (object? value in new object?[] { new ItemStack(f.Item, 1), 7, null, null })
            args.Parsers.Add(PlayerDefenseServiceTests.Proxy.Create<ICommandArgumentParser>((name, _) => name == "GetValue" ? value : null));
        var type = AccessTools.TypeByName("Vintagestory.Server.CmdGive");
        var result = (TextCommandResult)AccessTools.Method(type, "give").Invoke(RuntimeHelpers.GetUninitializedObject(type), new object[] { f.Entity, args })!;
        Assert.Equal(accepted ? EnumCommandStatus.Success : EnumCommandStatus.Error, result.Status);
        Assert.Equal(accepted ? 7 : 0, f.Inventory[0].StackSize);
    }

    [Fact]
    public void ExplicitClientManagerKeepsNativeLocalTransferAndDeathWithoutServerAuthority()
    {
        using var scope = new NativeMutationScope("throw");
        var f = new NativeMutationFixture { Side = EnumAppSide.Client };
        var manager = (ClientPlayerInventoryManager)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlayerInventoryManager));
        var client = (ClientPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlayer));
        var data = (ClientWorldPlayerData)RuntimeHelpers.GetUninitializedObject(typeof(ClientWorldPlayerData));
        data.PlayerUID = "player";
        data.EntityPlayer = f.Entity;
        AccessTools.Field(typeof(ClientPlayer), "worlddata").SetValue(client, data);
        manager.player = client;
        manager.Inventories = f.Manager.Inventories;
        Assert.True(manager.TryGiveItemstack(new(f.Item, 7)));
        Assert.Equal(7, f.Inventory[0].StackSize);
        f.Inventory[0].Itemstack = null;
        var source = new DummySlot(new ItemStack(f.Item, 3));
        var op = new ItemStackMoveOperation(f.World, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, 3);
        Assert.NotNull(manager.TryTransferAway(source, ref op, true));
        Assert.Equal(0, source.StackSize);
        manager.OnDeath();
        Assert.Equal(1, f.Inventory.DeathCalls);
    }

    [Theory]
    [InlineData("canonical")]
    [InlineData("recovery")]
    [InlineData("absent")]
    [InlineData("throw")]
    [InlineData("unknown")]
    [InlineData("vanilla")]
    public void ActualManagerDeathRefusesBeforeInventoryCallbacks(string custody)
    {
        using var scope = new NativeMutationScope(custody);
        var f = new NativeMutationFixture();
        if (custody == "unknown") f.Manager.player = null!;
        var stack = f.Inventory[1].Itemstack = new(f.Item, 3);
        f.Manager.OnDeath();
        Assert.Equal(custody == "vanilla" ? 1 : 0, f.Inventory.DeathCalls);
        if (custody != "vanilla") Assert.Same(stack, f.Inventory[1].Itemstack);
    }

    [Fact]
    public void ServerManagerCannotBorrowClientSidePlayerPermission()
    {
        using var scope = new NativeMutationScope("vanilla");
        var f = new NativeMutationFixture { Side = EnumAppSide.Client };
        f.Manager.player = (ClientPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlayer));
        var stack = new ItemStack(f.Item, 7);
        Assert.False(f.Manager.TryGiveItemstack(stack));
        Assert.Equal(7, stack.StackSize);
        Assert.Equal(0, f.Inventory[0].StackSize);
    }
    public static IEnumerable<object[]> TransferCases()
    {
        foreach (string custody in new[] { "canonical", "recovery", "absent", "throw", "unknown", "blank", "vanilla" })
        foreach (string route in new[] { "give", "short", "long" }) yield return new object[] { custody, route };
    }

    [Theory]
    [MemberData(nameof(TransferCases))]
    public void RealManagerGiveAndBothTransfersRetainRefusedSourceAndOperation(string custody, string route)
    {
        using var scope = new NativeMutationScope(custody);
        var f = new NativeMutationFixture();
        if (custody == "unknown") f.Manager.player = null!;
        if (custody == "blank") AccessTools.Field(typeof(ServerWorldPlayerData), "PlayerUID").SetValue(f.Player.WorldData, " ");
        var stack = new ItemStack(f.Item, 7);
        var source = new DummySlot(stack);
        var op = new ItemStackMoveOperation(f.World, EnumMouseButton.Right, EnumModifierKey.CTRL, EnumMergePriority.AutoMerge, 2);
        var originalOp = op;
        var debug = new StringBuilder("sentinel");
        bool accepted = route == "give" ? f.Manager.TryGiveItemstack(stack)
            : route == "short" ? f.Manager.TryTransferAway(source, ref op, true) != null
            : f.Manager.TryTransferAway(source, ref op, true, debug) != null;
        bool allowed = custody == "vanilla";
        Assert.Equal(allowed, accepted);
        Assert.Equal(allowed ? 7 : 0, f.Inventory[0].StackSize);
        Assert.Equal(allowed ? 0 : 7, route == "give" ? stack.StackSize : source.StackSize);
        Assert.Same(originalOp, op);
        if (!allowed)
        {
            Assert.Same(stack, source.Itemstack);
            Assert.Equal(2, op.RequestedQuantity);
            Assert.Equal(0, op.MovedQuantity);
            Assert.Equal(0, op.MovableQuantity);
            Assert.Equal("sentinel", debug.ToString());
            Assert.Empty(f.Inventory.DirtySlots);
            Assert.Equal(0, f.Inventory.ModifiedCalls);
            Assert.Equal(0, f.Events);
        }
        else if (route != "give") Assert.Equal(7, op.MovedQuantity);
        Assert.Equal(EnumMouseButton.Right, op.MouseButton);
        Assert.Equal(EnumModifierKey.CTRL, op.Modifiers);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RealDropItemRefusesBeforeCallbackAndTransfersSingleOrFullForVanilla(bool vanilla, bool full)
    {
        using var scope = new NativeMutationScope(vanilla ? "vanilla" : "canonical");
        var f = new NativeMutationFixture();
        var stack = f.Inventory[0].Itemstack = new(f.Item, 7);
        bool accepted = f.Manager.DropItem(f.Inventory[0], full);
        Assert.Equal(vanilla, accepted);
        Assert.Equal(vanilla ? 1 : 0, f.Item.DropCalls);
        Assert.Equal(vanilla ? full ? 0 : 6 : 7, f.Inventory[0].StackSize);
        Assert.Equal(vanilla ? full ? 7 : 1 : 0, f.Ground[0].StackSize);
        if (!vanilla)
        {
            Assert.Same(stack, f.Inventory[0].Itemstack);
            Assert.Equal(0, f.Inventory.ModifiedCalls);
            Assert.Empty(f.Inventory.DirtySlots);
        }
    }

    [Fact]
    public void NativeBodiesAndActualDeferredDelegateWitness()
    {
        var owners = new[] { typeof(PlayerInventoryManager), typeof(ServerPlayerInventoryManager), typeof(InventoryBasePlayer),
            typeof(InventoryPlayerHotbar), typeof(InventoryPlayerBackpacks), typeof(InventoryPlayerCreative),
            typeof(EntityBehaviorPlayerInventory), typeof(EntityPlayer), typeof(InventoryBase), AccessTools.TypeByName("Vintagestory.Server.CmdGive") };
        foreach (var owner in owners)
        foreach (var method in owner.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(m => m.Name is "TryGiveItemstack" or "TryTransferAway" or "DropItem" or "OnDeath" or "DropAll" or "OnEntityDeath" or "TryGiveItemStack" or "OnOwningEntityDeath" or "give"
                || m.Name.Contains("OnEntityDeath>")).Where(m => !m.IsAbstract))
        {
            output.WriteLine($"{method.DeclaringType!.FullName}.{method} MVID={method.Module.ModuleVersionId} SHA={Convert.ToHexString(SHA256.HashData(method.GetMethodBody()!.GetILAsByteArray()!))}");
            if (method.Name == "OnEntityDeath")
                foreach (var instruction in PatchProcessor.GetOriginalInstructions(method)) output.WriteLine(instruction.ToString());
        }
    }
}
