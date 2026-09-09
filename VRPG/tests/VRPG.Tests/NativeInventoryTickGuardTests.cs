using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using HarmonyLib;
using VRPG.Inventory;
using VRPG.Patches;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Server;
using Vintagestory.Common;
using Xunit;
using Xunit.Abstractions;

namespace VRPG.Tests;

[Collection("EngineItemBirth")]
public sealed class NativeInventoryTickGuardTests(ITestOutputHelper output)
{
    // Missing/late per-player refusal lets native callbacks consume canonical sources or
    // change controls. A whole-loop return loses the allowed player after the refusal.
    [Theory]
    [InlineData("idle", false)]
    [InlineData("use", false)]
    [InlineData("empty-use", false)]
    [InlineData("transition", false)]
    [InlineData("idle", true)]
    [InlineData("use", true)]
    [InlineData("empty-use", true)]
    [InlineData("transition", true)]
    public void ActualNativeMixedRosterPreservesRefusedPlayersAndLaterVanilla(string scenario, bool registeredDelegate)
    {
        using var scope = new TickScope();
        var roster = new[] { scope.Add("vanilla-first"), scope.Add("canonical"), scope.Add("recovery"),
            scope.Add("vanilla-last"), scope.Add("offline", false) };
        var resolvedOrder = new List<string>();
        scope.Resolve(uid =>
        {
            resolvedOrder.Add(uid);
            return new(uid.StartsWith("vanilla") || uid == "offline" ? PlayerItemCustodyOwner.Vanilla
                : uid == "canonical" ? PlayerItemCustodyOwner.Canonical : PlayerItemCustodyOwner.Recovery, false);
        });
        foreach (var fixture in roster)
        {
            var controls = fixture.Player.Entity.Controls;
            controls.LeftMouseDown = true;
            controls.HandUse = scenario is "use" or "empty-use" ? EnumHandInteract.HeldItemInteract : EnumHandInteract.None;
            fixture.Player.Entity.BlockSelection = new() { Position = new BlockPos(0, 0, 0, 0) };
            ((ServerWorldPlayerData)fixture.Player.WorldData).GameMode = EnumGameMode.Survival;
            fixture.Hotbar[11].Itemstack = new(fixture.Item, 2);
            if (scenario == "empty-use") fixture.Hotbar[0].Itemstack = null;
            AccessTools.Field(typeof(ServerPlayer), "blockBreakingCounter").SetValue(fixture.Player, 8);
        }
        scope.Run(scenario == "transition" ? "UpdateTransitionStates" : "OnUsingTick", registeredDelegate);
        foreach (var fixture in roster)
        {
            bool allowed = fixture.Player.PlayerUID.StartsWith("vanilla");
            bool tick = scenario != "transition" && allowed;
            Assert.Equal(tick && scenario != "empty-use" ? 1 : 0, fixture.Item.BreakingCalls);
            Assert.Equal(tick ? scenario == "idle" ? 2 : 1 : 0, fixture.Item.IdleSlots.Count);
            if (tick) Assert.Contains(fixture.Hotbar[11], fixture.Item.IdleSlots);
            if (tick && scenario == "idle") Assert.Contains(fixture.Hotbar[0], fixture.Item.IdleSlots);
            Assert.Equal(tick && scenario == "use" ? 1 : 0, fixture.Item.StepCalls);
            Assert.Equal(tick && scenario == "use" ? 1 : 0, fixture.Item.StopCalls);
            Assert.Equal(tick && scenario == "use" ? 1 : 0, fixture.Hotbar.ModifiedCalls);
            Assert.Equal(scenario == "empty-use" || tick && scenario == "use" ? 0 : 7, fixture.Hotbar[0].StackSize);
            Assert.Equal(scenario == "transition" && allowed ? 1 : 0, fixture.Item.TransitionCalls);
            Assert.Equal(scenario == "transition" && allowed ? 4 : 5, fixture.Cursor[0].StackSize);
            Assert.Equal(tick ? scenario == "empty-use" ? 0 : 9 : 8,
                (int)AccessTools.Field(typeof(ServerPlayer), "blockBreakingCounter").GetValue(fixture.Player)!);
            Assert.Equal(tick || scenario is "idle" or "transition" ? EnumHandInteract.None : EnumHandInteract.HeldItemInteract,
                fixture.Player.Entity.Controls.HandUse);
            Assert.Equal(tick && scenario == "use" ? 1 : 0, fixture.Player.Entity.Controls.UsingCount);
            Assert.True(fixture.Player.Entity.Controls.LeftMouseDown);
            Assert.Equal(fixture.Player.PlayerUID == "offline" ? EnumClientState.Offline : EnumClientState.Playing, fixture.Client.State);
        }
        Assert.Equal(5, scope.Server!.PlayersByUid.Count);
        Assert.Equal(5, scope.Server.Clients.Count);
        int firstRefused = resolvedOrder.FindIndex(uid => uid is "canonical" or "recovery");
        Assert.True(firstRefused >= 0);
        Assert.Contains(resolvedOrder.Skip(firstRefused + 1), uid => uid.StartsWith("vanilla"));
    }

    [Theory]
    [InlineData("OnUsingTick", "absent")]
    [InlineData("OnUsingTick", "throw")]
    [InlineData("OnUsingTick", "unknown")]
    [InlineData("UpdateTransitionStates", "absent")]
    [InlineData("UpdateTransitionStates", "throw")]
    [InlineData("UpdateTransitionStates", "unknown")]
    public void MissingIdentityOrAuthorityRefusesBeforeInventoryAccess(string method, string failure)
    {
        using var scope = new TickScope();
        var fixture = scope.Add(failure == "unknown" ? " " : "player");
        scope.Resolve(failure == "absent" ? null : failure == "throw" ? _ => throw new InvalidOperationException("unavailable")
            : _ => new(PlayerItemCustodyOwner.Vanilla, false));
        AccessTools.Field(typeof(ServerPlayer), "inventoryMgr").SetValue(fixture.Player, null);
        Assert.Null(Record.Exception(() => scope.Run(method)));
    }

    [Theory]
    [InlineData("OnUsingTick")]
    [InlineData("UpdateTransitionStates")]
    public void InstalledNativeBodyWitness(string name)
    {
        var method = AccessTools.Method(TickScope.NativeType, name);
        output.WriteLine(name + " " + method.Module.ModuleVersionId + " "
            + Convert.ToHexString(SHA256.HashData(method.GetMethodBody()!.GetILAsByteArray()!)));
        Assert.Equal(typeof(void), method.ReturnType);
        Assert.Equal(typeof(float), Assert.Single(method.GetParameters()).ParameterType);
    }

    [Theory]
    [InlineData("OnUsingTick", "remove")]
    [InlineData("UpdateTransitionStates", "remove")]
    [InlineData("OnUsingTick", "conflict")]
    [InlineData("UpdateTransitionStates", "conflict")]
    [InlineData(".ctor", "conflict")]
    public void ReadinessDetectsLostTickOrConstructorWitness(string name, string failure)
    {
        using var scope = new TickScope();
        scope.Resolve(_ => PlayerItemCustodySnapshot.Unresolved);
        Assert.True(NativeInventoryTickGuardPatch.IsReady);
        MethodBase target = name == ".ctor" ? AccessTools.Constructor(TickScope.NativeType, new[] { typeof(ServerMain) })
            : AccessTools.Method(TickScope.NativeType, name);
        if (failure == "remove") scope.Harmony.Unpatch(target, HarmonyPatchType.Transpiler, scope.Harmony.Id);
        else scope.Harmony.Patch(target, postfix: new HarmonyMethod(typeof(NativeInventoryTickGuardTests), nameof(ConflictPostfix)));
        Assert.False(NativeInventoryTickGuardPatch.IsReady);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-body")]
    [InlineData("conflict")]
    [InlineData("constructor")]
    [InlineData("late-conflict")]
    public void UnsupportedNativeShapeOrConflictingPatchCannotLeavePartialInstallation(string failure)
    {
        using var scope = new TickScope();
        NativeInventoryTickGuardPatch.Reset();
        if (failure is "conflict" or "constructor")
        {
            MethodBase target = failure == "constructor" ? AccessTools.Constructor(TickScope.NativeType, new[] { typeof(ServerMain) })
                : AccessTools.Method(TickScope.NativeType, "UpdateTransitionStates");
            scope.Harmony.Patch(target, postfix: new HarmonyMethod(typeof(NativeInventoryTickGuardTests), nameof(ConflictPostfix)));
        }
        NativeInventoryTickGuardPatch.Patch(scope.Harmony, TickScope.Logger,
            (type, name, args) =>
            {
                if (name == "UpdateTransitionStates" && failure == "late-conflict")
                    scope.Harmony.Patch(AccessTools.Method(type, "OnUsingTick"),
                        postfix: new HarmonyMethod(typeof(NativeInventoryTickGuardTests), nameof(ConflictPostfix)));
                return name == "UpdateTransitionStates" && failure is "missing" or "wrong-body"
                    ? failure == "missing" ? null : AccessTools.Method(type, "OnUsingTick", args)
                    : AccessTools.Method(type, name, args);
            });
        Assert.False(NativeInventoryTickGuardPatch.IsInstalled);
        Assert.DoesNotContain(Harmony.GetAllPatchedMethods(), method => Harmony.GetPatchInfo(method)?.Transpilers.Any(
            patch => patch.PatchMethod.DeclaringType == typeof(NativeInventoryTickGuardPatch)) == true);
        if (failure == "late-conflict")
            Assert.Single(Harmony.GetPatchInfo(AccessTools.Method(TickScope.NativeType, "OnUsingTick")).Postfixes);
    }

    [Theory]
    [InlineData("OnUsingTick")]
    [InlineData("UpdateTransitionStates")]
    public void NonPlayingPlayersRemainSkippedBeforeUnavailableInventoryAccess(string name)
    {
        using var scope = new TickScope();
        var fixture = scope.Add("offline", false);
        scope.Resolve(_ => new(PlayerItemCustodyOwner.Vanilla, false));
        AccessTools.Field(typeof(ServerPlayer), "inventoryMgr").SetValue(fixture.Player, null);
        Assert.Null(Record.Exception(() => scope.Run(name, true)));
        Assert.Equal(EnumClientState.Offline, fixture.Client.State);
    }

    [Theory]
    [InlineData("OnUsingTick", "missing")]
    [InlineData("UpdateTransitionStates", "missing")]
    [InlineData("OnUsingTick", "label")]
    [InlineData("UpdateTransitionStates", "label")]
    [InlineData("OnUsingTick", "exception")]
    [InlineData("UpdateTransitionStates", "exception")]
    [InlineData("OnUsingTick", "continuation")]
    [InlineData("UpdateTransitionStates", "continuation")]
    public void MalformedTranspilerInputThrowsInsteadOfAdvertisingANoOp(string name, string damage)
    {
        using var scope = new TickScope();
        scope.Resolve(_ => PlayerItemCustodySnapshot.Unresolved);
        var target = AccessTools.Method(TickScope.NativeType, name);
        var code = PatchProcessor.GetOriginalInstructions(target);
        int state = code.FindIndex(instruction => instruction.operand is MethodInfo { Name: "get_ConnectionState" or "get_IsPlayingClient" });
        if (damage == "missing") code.RemoveAt(state);
        if (damage == "label") code[state - 1].labels.Add(new DynamicMethod("label", typeof(void), Type.EmptyTypes).GetILGenerator().DefineLabel());
        if (damage == "exception") code[state - 1].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
        if (damage == "continuation") code.Last(instruction => instruction.operand is MethodInfo { Name: "MoveNext" }).operand = typeof(object).GetMethod("ToString");
        var error = Assert.Throws<TargetInvocationException>(() => AccessTools.Method(typeof(NativeInventoryTickGuardPatch), "Transpiler")
            .Invoke(null, new object[] { code, target }));
        Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.False(NativeInventoryTickGuardPatch.IsReady);
    }

    [Theory]
    [InlineData("OnUsingTick")]
    [InlineData("UpdateTransitionStates")]
    public void ResetAndReinstallCannotRetainOldAuthorityOrSkipMissingPlayer(string name)
    {
        using var scope = new TickScope();
        var fixture = scope.Add("player");
        scope.Resolve(_ => new(PlayerItemCustodyOwner.Vanilla, false));
        Assert.True(NativeInventoryTickGuardPatch.IsReady);
        NativeInventoryTickGuardPatch.Reset();
        Assert.Null(NativeInventoryTickGuardPatch.CustodyResolver);
        Assert.False(NativeInventoryTickGuardPatch.IsInstalled);
        NativeInventoryTickGuardPatch.Patch(scope.Harmony, TickScope.Logger);
        Assert.True(NativeInventoryTickGuardPatch.IsInstalled);
        Assert.False(NativeInventoryTickGuardPatch.IsReady);
        scope.Run(name, true);
        Assert.Empty(fixture.Item.IdleSlots);
        Assert.Equal(0, fixture.Item.TransitionCalls);
        scope.Resolve(_ => new(PlayerItemCustodyOwner.Vanilla, false));
        fixture.Client.Player = null;
        scope.Server!.PlayersByUid["player"] = null!;
        Assert.Null(Record.Exception(() => scope.Run(name, true)));
    }

    private static void ConflictPostfix() { }

    private sealed class TickScope : IDisposable
    {
        public static readonly Type NativeType = AccessTools.TypeByName("Vintagestory.Server.ServerSystemInventory");
        public static ILogger Logger => PlayerDefenseServiceTests.Proxy.Create<ILogger>((_, _) => null);
        public readonly Harmony Harmony = new("vrpg.tests.native-tick." + Guid.NewGuid());
        public readonly object Native = RuntimeHelpers.GetUninitializedObject(NativeType);
        private readonly Dictionary<string, GameTickListener> registered;
        public ServerMain? Server;
        public TickScope()
        {
            var bootstrap = new NativeInventoryGuardFixture(Native);
            var packetField = AccessTools.Field(typeof(ServerMain), "PacketHandlers");
            packetField.SetValue(bootstrap.Server, Array.CreateInstance(packetField.FieldType.GetElementType()!, 255));
            int oldThread = RuntimeEnv.ServerMainThreadId;
            RuntimeEnv.ServerMainThreadId = Environment.CurrentManagedThreadId;
            try { Native = AccessTools.Constructor(NativeType, new[] { typeof(ServerMain) }).Invoke(new object[] { bootstrap.Server }); }
            finally { RuntimeEnv.ServerMainThreadId = oldThread; }
            registered = ((List<GameTickListener>)AccessTools.Field(typeof(EventManager), "GameTickListenersEntity").GetValue(bootstrap.Events)!)
                .ToDictionary(listener => listener.Handler.Method.Name);
            Assert.Equal(3, registered.Count);
            Assert.Equal(20, registered["OnUsingTick"].Millisecondinterval);
            Assert.Equal(4000, registered["UpdateTransitionStates"].Millisecondinterval);
            // These are native constructor-created delegates captured BEFORE Harmony installation.
            NativeInventoryTickGuardPatch.Patch(Harmony, Logger);
            Assert.True(NativeInventoryTickGuardPatch.IsInstalled);
        }
        public void Resolve(System.Func<string, PlayerItemCustodySnapshot>? resolver) => NativeInventoryTickGuardPatch.CustodyResolver = resolver;
        public NativeInventoryGuardFixture Add(string uid, bool playing = true)
        {
            var fixture = new NativeInventoryGuardFixture(Native, uid);
            fixture.Item.ObserveTicks = true;
            if (Server == null)
            {
                Server = fixture.Server;
                Server.PlayersByUid = new();
                Server.Clients = new();
                var api = (ServerCoreAPI)RuntimeHelpers.GetUninitializedObject(typeof(ServerCoreAPI));
                AccessTools.Field(typeof(ServerCoreAPI), "server").SetValue(api, Server);
                AccessTools.Field(typeof(ServerMain), "api").SetValue(Server, api);
                AccessTools.Field(typeof(ServerMain), "SaveGameData").SetValue(Server, new SaveGame { WorldConfiguration = new TreeAttribute() });
            }
            AccessTools.Field(typeof(ServerSystem), "server").SetValue(Native, Server);
            Server.PlayersByUid[uid] = fixture.Player;
            Server.Clients[Server.Clients.Count + 1] = fixture.Client;
            AccessTools.Field(typeof(ServerPlayer), "client").SetValue(fixture.Player, fixture.Client);
            fixture.Client.State = playing ? EnumClientState.Playing : EnumClientState.Offline;
            fixture.Player.Entity.World = Server;
            fixture.Player.Entity.WatchedAttributes.SetString("playerUID", uid);
            return fixture;
        }
        public void Run(string name, bool registeredDelegate = false)
        {
            if (registeredDelegate) registered[name].OnTriggered(20);
            else AccessTools.Method(NativeType, name).Invoke(Native, new object[] { 0.02f });
        }
        public void Dispose()
        {
            Harmony.UnpatchAll(Harmony.Id);
            NativeInventoryTickGuardPatch.Reset();
        }
    }
}
