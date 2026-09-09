using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using HarmonyLib;
using VRPG.Patches;
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
        using var scope = new NativeMutationScope("vanilla");
        using var native = new NativeClearCommandFixture();
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
        var (clear, callback, playerClear, playerCallback) = AssertRegistrationEvidence(code, native);
        methods.Add(callback);
        methods.Add(playerCallback);
        methods.Add(AccessTools.Method(typeof(CmdPlayer), "Each"));
        Assert.True(NativeInventoryMutationGuardPatch.IsReady);
        var installation = AccessTools.Field(typeof(NativeInventoryMutationGuardPatch), "installation").GetValue(null);
        Assert.NotNull(installation);
        var clearWitnesses = new MethodBase[] { ctor, AccessTools.Method(typeof(InventoryCharacter), "DiscardAll"),
            AccessTools.Method(typeof(InventoryPlayerCreative), "DiscardAll"), AccessTools.Method(typeof(CmdPlayer), "Each") };
        var previousTargets = NativeInventoryMutationInstallationTests.Targets()
            .Select(row => (MethodBase)NativeInventoryMutationInstallationTests.Target((string)row[0])).ToHashSet();
        var expectedTargets = methods.Except(clearWitnesses).Concat(previousTargets).ToHashSet();
        var expectedWitnesses = NativeInventoryMutationInstallationTests.Boundaries()
            .Select(row => (MethodBase)NativeInventoryMutationInstallationTests.Target((string)row[0]))
            .Except(previousTargets).Concat(clearWitnesses).ToHashSet();
        var actualTargets = (MethodInfo[])AccessTools.Property(installation.GetType(), "Targets").GetValue(installation)!;
        var actualWitnesses = (MethodBase[])AccessTools.Property(installation.GetType(), "Witnesses").GetValue(installation)!;
        Assert.Equal(expectedTargets.Count, actualTargets.Length);
        Assert.True(expectedTargets.SetEquals(actualTargets), "Every required native effect and registered clear callback must be guarded.");
        Assert.Equal(expectedWitnesses.Count, actualWitnesses.Length);
        Assert.True(expectedWitnesses.SetEquals(actualWitnesses), "Every required native routing/empty-override witness must remain monitored.");
        AssertQueueEvidence(PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(ServerWorldPlayerData), "Init")));
        foreach (var method in methods)
        {
            output.WriteLine($"{method.DeclaringType!.FullName}.{method} MVID={method.Module.ModuleVersionId} SHA={Convert.ToHexString(SHA256.HashData(method.GetMethodBody()!.GetILAsByteArray()!))}");
            if (method.Name is "Init" || method == callback || method == playerCallback)
                foreach (var instruction in PatchProcessor.GetOriginalInstructions(method)) output.WriteLine(instruction.ToString());
        }
        foreach (var instruction in code.Skip(clear - 12).Take(30)) output.WriteLine(instruction.ToString());
        foreach (var instruction in code.Skip(playerClear - 4).Take(11)) output.WriteLine(instruction.ToString());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong")]
    public void RegistrationEvidenceRejectsMissingOrWrongDiscovery(string mutation)
    {
        using var native = new NativeClearCommandFixture();
        var code = PatchProcessor.GetOriginalInstructions(AccessTools.Constructor(typeof(CmdPlayer), new[] { typeof(ServerMain) }));
        int selected = code.FindIndex(i => i.opcode == OpCodes.Ldstr && Equals(i.operand, "Clear the player's entire inventory"));
        if (mutation == "missing") code.RemoveAt(selected);
        else code.Skip(selected).First(i => i.opcode == OpCodes.Ldftn).operand =
            ((OnCommandDelegate)AccessTools.Field(typeof(ChatCommandImpl), "handler").GetValue(native.Api.ChatCommands.Get("clearinv"))!).Method;
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertRegistrationEvidence(code, native));
    }

    private static (int Clear, MethodInfo Callback, int PlayerClear, MethodInfo PlayerCallback) AssertRegistrationEvidence(
        List<CodeInstruction> code, NativeClearCommandFixture native)
    {
        int clear = Assert.Single(Enumerable.Range(0, code.Count)
            .Where(i => code[i].opcode == OpCodes.Ldstr && Equals(code[i].operand, "Empties your inventory")));
        var callback = (MethodInfo)code.Skip(clear).First(i => i.opcode == OpCodes.Ldftn).operand;
        int playerClear = Assert.Single(Enumerable.Range(0, code.Count)
            .Where(i => code[i].opcode == OpCodes.Ldstr && Equals(code[i].operand, "Clear the player's entire inventory")));
        var playerCallback = (MethodInfo)code.Skip(playerClear).First(i => i.opcode == OpCodes.Ldftn).operand;
        var handler = AccessTools.Field(typeof(ChatCommandImpl), "handler");
        Assert.Equal(((OnCommandDelegate)handler.GetValue(native.Api.ChatCommands.Get("clearinv"))!).Method, callback);
        Assert.Equal(((OnCommandDelegate)handler.GetValue(native.Api.ChatCommands.Get("player")["clearinv"])!).Method, playerCallback);
        Assert.NotEqual(callback, playerCallback);
        Assert.Equal(typeof(CmdPlayer), callback.DeclaringType?.DeclaringType);
        Assert.Equal(typeof(CmdPlayer), playerCallback.DeclaringType?.DeclaringType);
        Assert.Single(PatchProcessor.GetOriginalInstructions(callback), i => i.Calls(
            AccessTools.Method(typeof(IPlayerInventoryManager), "DiscardAll")));
        var selectedBody = PatchProcessor.GetOriginalInstructions(playerCallback);
        Assert.Equal(AccessTools.Method(typeof(CmdPlayer), "WipePlayerInventory"),
            Assert.Single(selectedBody, i => i.opcode == OpCodes.Ldftn).operand);
        Assert.Single(selectedBody, i => i.Calls(AccessTools.Method(typeof(CmdPlayer), "Each")));
        return (clear, callback, playerClear, playerCallback);
    }

    private static void AssertQueueEvidence(List<CodeInstruction> code)
    {
        int remove = Assert.Single(Enumerable.Range(0, code.Count).Where(i => code[i].Calls(
            AccessTools.Method(typeof(HashSet<string>), "Remove"))));
        int clear = Assert.Single(Enumerable.Range(0, code.Count).Where(i => code[i].Calls(
            AccessTools.Method(typeof(InventoryBase), "Clear"))));
        Assert.True(code[remove - 3].LoadsField(AccessTools.Field(typeof(ServerMain), "ClearPlayerInvs")));
        Assert.True(code[remove - 1].LoadsField(AccessTools.Field(typeof(ServerWorldPlayerData), "PlayerUID")));
        Assert.Equal(OpCodes.Stloc_0, code[remove + 1].opcode);
        Assert.Equal(OpCodes.Ldloc_0, code[clear - 3].opcode);
        Assert.Equal(OpCodes.Brfalse_S, code[clear - 2].opcode);
        Assert.Contains((Label)code[clear - 2].operand, code[clear + 1].labels);
        int deserialize = Assert.Single(Enumerable.Range(0, code.Count).Where(i => code[i].Calls(
            AccessTools.Method(typeof(InventoryBase), "FromTreeAttributes"))));
        Assert.True(remove < deserialize && deserialize < clear, "Queued decision must precede deserialization and its conditional clear.");
    }
}
