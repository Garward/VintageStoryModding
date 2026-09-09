using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using VRPG.Patches;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.Common;
using Vintagestory.GameContent;
using Vintagestory.Server;
using Xunit;

namespace VRPG.Tests;

[Collection("EngineItemBirth")]
public sealed class NativeInventoryMutationInstallationTests
{
    public static IEnumerable<object[]> Targets()
    {
        foreach (string name in new[] { "give", "transfer", "drop", "death", "base", "hotbar", "backpack", "deferred" })
            yield return new object[] { name };
    }
    public static IEnumerable<object[]> Boundaries()
    {
        foreach (var target in Targets()) yield return target;
        foreach (string name in new[] { "short", "creative", "enqueue", "owning-death", "entity-give", "command" })
            yield return new object[] { name };
    }

    internal static MethodInfo Target(string name) => name switch
    {
        "give" => AccessTools.Method(typeof(PlayerInventoryManager), "TryGiveItemstack"),
        "transfer" => typeof(PlayerInventoryManager).GetMethods().Single(m => m.Name == "TryTransferAway" && m.GetParameters().Length == 5),
        "short" => typeof(PlayerInventoryManager).GetMethods().Single(m => m.Name == "TryTransferAway" && m.GetParameters().Length == 4),
        "drop" => AccessTools.Method(typeof(ServerPlayerInventoryManager), "DropItem"),
        "death" => AccessTools.Method(typeof(PlayerInventoryManager), "OnDeath"),
        "base" => AccessTools.Method(typeof(InventoryBasePlayer), "DropAll"),
        "hotbar" => AccessTools.Method(typeof(InventoryPlayerHotbar), "DropAll"),
        "backpack" => AccessTools.Method(typeof(InventoryPlayerBackpacks), "DropAll"),
        "creative" => AccessTools.Method(typeof(InventoryPlayerCreative), "DropAll"),
        "enqueue" => AccessTools.Method(typeof(EntityBehaviorPlayerInventory), "OnEntityDeath"),
        "deferred" => (MethodInfo)PatchProcessor.GetOriginalInstructions(Target("enqueue")).Single(i => i.opcode == OpCodes.Ldftn).operand,
        "owning-death" => AccessTools.Method(typeof(InventoryBase), "OnOwningEntityDeath"),
        "entity-give" => AccessTools.Method(typeof(EntityPlayer), "TryGiveItemStack"),
        "command" => AccessTools.Method(AccessTools.TypeByName("Vintagestory.Server.CmdGive"), "give"),
        _ => throw new ArgumentException(name)
    };

    [Theory]
    [MemberData(nameof(Targets))]
    public void EveryLostEffectPrefixWithdrawsReadiness(string name)
    {
        using var scope = new NativeMutationScope("vanilla");
        Assert.True(NativeInventoryMutationGuardPatch.IsReady);
        scope.Harmony.Unpatch(Target(name), HarmonyPatchType.Prefix, scope.Harmony.Id);
        Assert.False(NativeInventoryMutationGuardPatch.IsInstalled);
        Assert.False(NativeInventoryMutationGuardPatch.IsReady);
    }

    [Theory]
    [MemberData(nameof(Boundaries))]
    public void EveryConflictingEffectOrRouteWithdrawsReadiness(string name)
    {
        using var scope = new NativeMutationScope("vanilla");
        Assert.True(NativeInventoryMutationGuardPatch.IsReady);
        scope.Harmony.Patch(Target(name), postfix: new HarmonyMethod(typeof(NativeInventoryMutationInstallationTests), nameof(Conflict)));
        Assert.False(NativeInventoryMutationGuardPatch.IsReady);
    }

    [Theory]
    [MemberData(nameof(Boundaries))]
    public void ExistingConflictCannotLeavePartialProtection(string name)
    {
        using var scope = new NativeMutationScope("vanilla");
        NativeInventoryMutationGuardPatch.Reset();
        scope.Harmony.Patch(Target(name), postfix: new HarmonyMethod(typeof(NativeInventoryMutationInstallationTests), nameof(Conflict)));
        NativeInventoryMutationGuardPatch.Patch(scope.Harmony, QuietLogger);
        Assert.False(NativeInventoryMutationGuardPatch.IsInstalled);
        AssertNoOwnedPrefixes();
        Assert.Single(Harmony.GetPatchInfo(Target(name)).Postfixes);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-body")]
    [InlineData("late-conflict")]
    public void UnsupportedResolutionAndLateConflictRemoveAnyPartialInstall(string failure)
    {
        using var scope = new NativeMutationScope("vanilla");
        NativeInventoryMutationGuardPatch.Reset();
        NativeInventoryMutationGuardPatch.Patch(scope.Harmony, QuietLogger, (type, name, args) =>
        {
            if (name == "give")
            {
                if (failure == "late-conflict") scope.Harmony.Patch(Target("give"), postfix: new HarmonyMethod(typeof(NativeInventoryMutationInstallationTests), nameof(Conflict)));
                if (failure == "missing") return null;
                if (failure == "wrong-body") return Target("entity-give");
            }
            return AccessTools.Method(type, name, args);
        });
        Assert.False(NativeInventoryMutationGuardPatch.IsInstalled);
        AssertNoOwnedPrefixes();
    }

    [Fact]
    public void NoOpHarmonyInstallationCannotAdvertiseProtection()
    {
        using var scope = new NativeMutationScope("vanilla");
        NativeInventoryMutationGuardPatch.Reset();
        var interceptor = new Harmony("vrpg.tests.no-op-installer." + Guid.NewGuid());
        var patchMethod = AccessTools.Method(typeof(Harmony), nameof(Harmony.Patch));
        try
        {
            interceptor.Patch(patchMethod, prefix: new HarmonyMethod(typeof(NativeInventoryMutationInstallationTests), nameof(SkipPatch)));
            NativeInventoryMutationGuardPatch.Patch(scope.Harmony, QuietLogger);
            Assert.False(NativeInventoryMutationGuardPatch.IsInstalled);
            AssertNoOwnedPrefixes();
        }
        finally { interceptor.Unpatch(patchMethod, HarmonyPatchType.Prefix, interceptor.Id); }
    }

    [Fact]
    public void ResetReinstallHasNoOldWorldAuthorityAndRefusesBeforeSourceEffects()
    {
        using var scope = new NativeMutationScope("vanilla");
        Assert.True(NativeInventoryMutationGuardPatch.IsReady);
        NativeInventoryMutationGuardPatch.Reset();
        Assert.Null(NativeInventoryMutationGuardPatch.CustodyResolver);
        AssertNoOwnedPrefixes();
        NativeInventoryMutationGuardPatch.Patch(scope.Harmony, NativeMutationScope.Logger);
        Assert.True(NativeInventoryMutationGuardPatch.IsInstalled);
        Assert.False(NativeInventoryMutationGuardPatch.IsReady);
        var f = new NativeMutationFixture();
        var stack = new ItemStack(f.Item, 7);
        Assert.False(f.Manager.TryGiveItemstack(stack));
        Assert.Equal(7, stack.StackSize);
        Assert.Equal(0, f.Inventory[0].StackSize);
        scope.Resolve("vanilla");
        Assert.True(f.Manager.TryGiveItemstack(stack));
        Assert.Equal(7, f.Inventory[0].StackSize);
    }

    [Fact]
    public void WrongDeferredProducerIsRejectedInsteadOfGuessingCallbackName()
    {
        var error = Assert.Throws<TargetInvocationException>(() => AccessTools.Method(typeof(NativeInventoryMutationGuardPatch), "FindDeathCallback")
            .Invoke(null, new object[] { Target("death") }));
        Assert.IsType<InvalidOperationException>(error.InnerException);
    }

    private static ILogger QuietLogger => PlayerDefenseServiceTests.Proxy.Create<ILogger>((_, _) => null);
    private static bool SkipPatch() => false;
    private static void Conflict() { }
    private static void AssertNoOwnedPrefixes() => Assert.DoesNotContain(Harmony.GetAllPatchedMethods(), method => Harmony.GetPatchInfo(method)?.Prefixes
        .Any(patch => patch.PatchMethod.DeclaringType == typeof(NativeInventoryMutationGuardPatch)) == true);
}
