using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using VRPG.Patches;
using Vintagestory.API.Common;
using Vintagestory.Common;
using Vintagestory.Server;
using Xunit;

namespace VRPG.Tests;

[Collection("EngineItemBirth")]
public sealed class NativeClearInstallationTests
{
    public static IEnumerable<object[]> Targets()
    {
        foreach (string name in new[] { "manager", "clear", "discard", "backpack", "wipe", "callback", "player-callback", "init" })
            yield return new object[] { name };
    }
    public static IEnumerable<object[]> Boundaries()
    {
        foreach (var target in Targets()) yield return target;
        foreach (string name in new[] { "character", "creative", "registration", "each" }) yield return new object[] { name };
    }
    internal static MethodBase Target(string name) => name switch
    {
        "manager" => AccessTools.Method(typeof(PlayerInventoryManager), "DiscardAll"),
        "clear" => AccessTools.Method(typeof(InventoryBase), "Clear"),
        "discard" => AccessTools.Method(typeof(InventoryBase), "DiscardAll"),
        "backpack" => AccessTools.Method(typeof(InventoryPlayerBackpacks), "DiscardAll"),
        "character" => AccessTools.Method(typeof(InventoryCharacter), "DiscardAll"),
        "creative" => AccessTools.Method(typeof(InventoryPlayerCreative), "DiscardAll"),
        "wipe" => AccessTools.Method(typeof(CmdPlayer), "WipePlayerInventory"),
        "init" => AccessTools.Method(typeof(ServerWorldPlayerData), "Init"),
        "registration" => AccessTools.Constructor(typeof(CmdPlayer), new[] { typeof(ServerMain) }),
        "callback" => Callback(),
        "player-callback" => Callback(true),
        "each" => AccessTools.Method(typeof(CmdPlayer), "Each"),
        _ => throw new ArgumentException(name)
    };
    private static MethodInfo Callback(bool selected = false)
    {
        var code = PatchProcessor.GetOriginalInstructions(Target("registration"));
        int start = code.FindIndex(i => i.opcode == OpCodes.Ldstr && Equals(i.operand,
            selected ? "Clear the player's entire inventory" : "Empties your inventory"));
        return (MethodInfo)code.Skip(start).First(i => i.opcode == OpCodes.Ldftn).operand;
    }

    [Theory]
    [MemberData(nameof(Targets))]
    public void LostClearHookWithdrawsReadiness(string boundary)
    {
        using var scope = new NativeMutationScope("vanilla");
        Assert.True(NativeInventoryMutationGuardPatch.IsReady);
        scope.Harmony.Unpatch(Target(boundary), boundary == "init" ? HarmonyPatchType.Transpiler : HarmonyPatchType.Prefix, scope.Harmony.Id);
        Assert.False(NativeInventoryMutationGuardPatch.IsReady);
    }

    [Theory]
    [InlineData("wipe", HarmonyPatchType.Postfix)]
    [InlineData("wipe", HarmonyPatchType.Finalizer)]
    [InlineData("player-callback", HarmonyPatchType.Postfix)]
    [InlineData("player-callback", HarmonyPatchType.Finalizer)]
    [InlineData("callback", HarmonyPatchType.Postfix)]
    [InlineData("callback", HarmonyPatchType.Finalizer)]
    public void LostResultOrCleanupHookWithdrawsReadiness(string boundary, HarmonyPatchType patch)
    {
        using var scope = new NativeMutationScope("vanilla");
        scope.Harmony.Unpatch(Target(boundary), patch, scope.Harmony.Id);
        Assert.False(NativeInventoryMutationGuardPatch.IsInstalled);
    }

    [Theory]
    [MemberData(nameof(Boundaries))]
    public void ConflictAtAnyClearBoundaryRefusesInstallAndRemovesOnlyOwnedHooks(string boundary)
    {
        using var scope = new NativeMutationScope("vanilla");
        scope.Harmony.Patch(Target(boundary), postfix: new HarmonyMethod(typeof(NativeClearInstallationTests), nameof(Conflict)));
        Assert.False(NativeInventoryMutationGuardPatch.IsReady);
        NativeInventoryMutationGuardPatch.Patch(scope.Harmony, QuietLogger);
        Assert.False(NativeInventoryMutationGuardPatch.IsInstalled);
        AssertNoOwnedHooks();
        Assert.Single(Harmony.GetPatchInfo(Target(boundary)).Postfixes);
    }

    [Theory]
    [InlineData("Clear")]
    [InlineData("DiscardAll")]
    [InlineData("WipePlayerInventory")]
    [InlineData("Init")]
    public void MissingClearTargetCannotLeavePartialProtection(string missing)
    {
        using var scope = new NativeMutationScope("vanilla");
        NativeInventoryMutationGuardPatch.Patch(scope.Harmony, QuietLogger,
            (type, name, args) => name == missing ? null : AccessTools.Method(type, name, args));
        Assert.False(NativeInventoryMutationGuardPatch.IsInstalled);
        AssertNoOwnedHooks();
    }

    [Fact]
    public void NoOpQueueTransformationCannotAdvertiseOrLeavePartialProtection()
    {
        using var scope = new NativeMutationScope("vanilla");
        NativeInventoryMutationGuardPatch.Reset();
        var transpiler = AccessTools.Method(typeof(NativeInventoryMutationGuardPatch), "QueueTranspiler");
        try
        {
            scope.Harmony.Patch(transpiler, prefix: new HarmonyMethod(typeof(NativeClearInstallationTests), nameof(NoOpTransform)));
            NativeInventoryMutationGuardPatch.Patch(scope.Harmony, QuietLogger);
            Assert.False(NativeInventoryMutationGuardPatch.IsInstalled);
            AssertNoOwnedHooks();
        }
        finally { scope.Harmony.Unpatch(transpiler, HarmonyPatchType.All, scope.Harmony.Id); }
        NativeInventoryMutationGuardPatch.Patch(scope.Harmony, NativeMutationScope.Logger);
        Assert.True(NativeInventoryMutationGuardPatch.IsInstalled);
        Assert.False(NativeInventoryMutationGuardPatch.IsReady);
    }

    [Fact]
    public void ChangedQueueShapeIsRejectedBeforeTransformation()
    {
        var code = PatchProcessor.GetOriginalInstructions(Target("init"));
        code.RemoveAll(i => i.Calls(AccessTools.Method(typeof(HashSet<string>), "Remove")));
        var error = Assert.Throws<TargetInvocationException>(() => AccessTools.Method(typeof(NativeInventoryMutationGuardPatch), "FindQueueSites")
            .Invoke(null, new object[] { code }));
        Assert.IsType<InvalidOperationException>(error.InnerException);
    }

    private static ILogger QuietLogger => PlayerDefenseServiceTests.Proxy.Create<ILogger>((_, _) => null);
    private static void Conflict() { }
    private static bool NoOpTransform(IEnumerable<CodeInstruction> instructions, ref IEnumerable<CodeInstruction> __result)
    { __result = instructions; return false; }
    private static void AssertNoOwnedHooks() => Assert.DoesNotContain(Harmony.GetAllPatchedMethods(), method =>
        Harmony.GetPatchInfo(method) is { } info && info.Prefixes.Concat(info.Transpilers).Concat(info.Postfixes).Concat(info.Finalizers)
            .Any(p => p.PatchMethod.DeclaringType == typeof(NativeInventoryMutationGuardPatch)));
}
