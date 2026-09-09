using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.Common;
using Vintagestory.Server;
using Xunit;
using Xunit.Abstractions;

namespace VRPG.Tests;

[Collection("EngineItemBirth")]
public sealed class NativeInventoryClearGuardTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string route in new[] { "manager", "clear", "discard", "backpack" })
        foreach (string custody in new[] { "canonical", "recovery", "vanilla", "absent", "throw", "unknown", "client" })
            yield return new object[] { route, custody };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void DirectClearPreservesProtectedSourceAndAllowsVerifiedVanilla(string route, string custody)
    {
        using var scope = new NativeMutationScope(custody == "client" ? "canonical" : custody);
        var f = new NativeMutationFixture();
        if (custody == "unknown") f.OwnerAvailable = false;
        if (custody == "client") f.Side = EnumAppSide.Client;
        InventoryBase inventory = route == "backpack" ? new InventoryPlayerBackpacks("backpack-player", null) { Api = f.Api } : f.Inventory;
        var stack = new ItemStack(f.Item, 7);
        inventory[0].Itemstack = stack;
        inventory.DirtySlots.Clear();
        if (route == "manager")
        {
            if (custody == "unknown") f.Manager.player = null;
            if (custody == "client")
            {
                var client = (Vintagestory.Client.NoObf.ClientPlayerInventoryManager)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Vintagestory.Client.NoObf.ClientPlayerInventoryManager));
                AccessTools.Field(typeof(PlayerInventoryManager), "Inventories").SetValue(client, f.Manager.Inventories);
                client.DiscardAll();
            }
            else f.Manager.DiscardAll();
        }
        else if (route == "clear") inventory.Clear();
        else inventory.DiscardAll();
        bool allowed = custody is "vanilla" or "client";
        if (allowed) Assert.Null(inventory[0].Itemstack);
        else
        {
            Assert.Same(stack, inventory[0].Itemstack);
            Assert.Equal(7, stack.StackSize);
            Assert.Empty(inventory.DirtySlots);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryContainerRemainsClearable(bool discard)
    {
        using var scope = new NativeMutationScope("canonical");
        var f = new NativeMutationFixture();
        f.Ground[0].Itemstack = new ItemStack(f.Item, 7);
        if (discard) f.Ground.DiscardAll(); else f.Ground.Clear();
        Assert.Null(f.Ground[0].Itemstack);
    }

    [Fact]
    public void ManagerRefusesBeforeCallingIndependentInventoryOverride()
    {
        using var scope = new NativeMutationScope("canonical");
        var f = new NativeMutationFixture();
        var observed = new DiscardObserver();
        f.Manager.Inventories.Add(observed.InventoryID, observed);
        f.Manager.DiscardAll();
        Assert.Equal(0, observed.Calls);
        scope.Resolve("vanilla");
        f.Manager.DiscardAll();
        Assert.Equal(1, observed.Calls);
    }

    private sealed class DiscardObserver() : InventoryPlayerMouseCursor("mouse", "player", null!)
    {
        public int Calls;
        public override void DiscardAll() => Calls++;
    }

    [Fact]
    public void InstalledClearBodiesAndRegistrationEvidence()
    {
        var ctor = AccessTools.Constructor(typeof(CmdPlayer), new[] { typeof(ServerMain) });
        var methods = new List<MethodBase> { ctor,
            AccessTools.Method(typeof(PlayerInventoryManager), "DiscardAll"),
            AccessTools.Method(typeof(InventoryBase), "Clear"),
            AccessTools.Method(typeof(InventoryBase), "DiscardAll"),
            AccessTools.Method(typeof(InventoryPlayerBackpacks), "DiscardAll"),
            AccessTools.Method(typeof(InventoryCharacter), "DiscardAll"),
            AccessTools.Method(typeof(InventoryPlayerCreative), "DiscardAll"),
            AccessTools.Method(typeof(CmdPlayer), "WipePlayerInventory"),
            AccessTools.Method(typeof(ServerWorldPlayerData), "Init") };
        var code = PatchProcessor.GetOriginalInstructions(ctor);
        int clear = code.FindIndex(i => i.opcode == OpCodes.Ldstr && Equals(i.operand, "Empties your inventory"));
        var callback = (MethodInfo)code.Skip(clear).First(i => i.opcode == OpCodes.Ldftn).operand;
        methods.Add(callback);
        int playerClear = code.FindIndex(i => i.opcode == OpCodes.Ldstr && Equals(i.operand, "Clear the player's entire inventory"));
        var playerCallback = (MethodInfo)code.Skip(playerClear).First(i => i.opcode == OpCodes.Ldftn).operand;
        methods.Add(playerCallback);
        methods.Add(AccessTools.Method(typeof(CmdPlayer), "Each"));
        foreach (var method in methods)
        {
            output.WriteLine($"{method.DeclaringType!.FullName}.{method} MVID={method.Module.ModuleVersionId} SHA={Convert.ToHexString(SHA256.HashData(method.GetMethodBody()!.GetILAsByteArray()!))}");
            if (method.Name is "Init" || method == callback || method == playerCallback)
                foreach (var instruction in PatchProcessor.GetOriginalInstructions(method)) output.WriteLine(instruction.ToString());
        }
        foreach (var instruction in code.Skip(clear - 12).Take(30)) output.WriteLine(instruction.ToString());
        foreach (var instruction in code.Skip(playerClear - 4).Take(11)) output.WriteLine(instruction.ToString());
    }
}
