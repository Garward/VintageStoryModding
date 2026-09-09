using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using HarmonyLib;
using VRPG.Inventory;
using VRPG.Patches;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.Server;
using Xunit;
using Xunit.Abstractions;

namespace VRPG.Tests;

[Collection("EngineItemBirth")]
public sealed class NativeWorldItemPacketGuardTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> RefusedCases()
    {
        foreach (string method in new[] { "HandleBlockPlaceOrBreak", "HandleBlockEntityPacket", "HandleEntityPacket" })
            foreach (var owner in new[] { PlayerItemCustodyOwner.Canonical, PlayerItemCustodyOwner.Recovery })
                yield return new object[] { method, owner };
    }

    // Removing any new target prefix would let malformed payload access occur before custody refusal.
    [Theory]
    [MemberData(nameof(RefusedCases))]
    public void ActualNativeEntrypointsRefuseBeforeMalformedPayloadAndTargetAccess(
        string name, PlayerItemCustodyOwner owner)
    {
        using var scope = new WorldGuardScope();
        string? resolvedUid = null;
        NativeInventoryPacketGuardPatch.CustodyResolver = uid =>
        {
            resolvedUid = uid;
            return new(owner, false);
        };

        Assert.Null(Record.Exception(() => scope.Invoke(name, null!)));
        Assert.Equal("player", resolvedUid);
        Assert.Equal(7, scope.Fixture.Hotbar[0].StackSize);
        Assert.Equal(0, scope.BlockEntity.Calls);
        Assert.Equal(0, scope.Entity.Calls);
        Assert.Equal(11, scope.BlockEntity.ExternalQuantity);
        Assert.Equal(11, scope.Entity.ExternalQuantity);
        Assert.Equal(0, scope.Fixture.Events.WorldAccessCalls);
    }

    public static IEnumerable<object[]> UnknownAuthorityCases()
    {
        foreach (string method in new[] { "HandleBlockPlaceOrBreak", "HandleBlockEntityPacket", "HandleEntityPacket" })
            foreach (string failure in new[] { "missing-player", "blank-identity", "absent-resolver", "throwing-resolver" })
                yield return new object[] { method, failure };
    }

    [Theory]
    [MemberData(nameof(UnknownAuthorityCases))]
    public void UnknownActorOrAuthorityNeverInfersVanillaFromPayloadOrTarget(string method, string failure)
    {
        using var scope = new WorldGuardScope(failure == "blank-identity" ? " " : "player");
        NativeInventoryPacketGuardPatch.CustodyResolver = failure switch
        {
            "absent-resolver" => null,
            "throwing-resolver" => _ => throw new InvalidOperationException("unavailable"),
            _ => _ => new(PlayerItemCustodyOwner.Vanilla, false)
        };
        if (failure == "missing-player") scope.Fixture.Client.Player = null;

        Packet_Client packet = method switch
        {
            "HandleBlockPlaceOrBreak" => new() { BlockPlaceOrBreak = new Packet_ClientBlockPlaceOrBreak { OnBlockFace = 0, Mode = 2 } },
            "HandleBlockEntityPacket" => new() { BlockEntityPacket = new Packet_BlockEntityPacket { Packetid = 37, Data = new byte[] { 1 } } },
            _ => new() { EntityPacket = new Packet_EntityPacket { EntityId = scope.Entity.EntityId, Packetid = 41, Data = new byte[] { 3 } } }
        };
        Assert.Null(Record.Exception(() => scope.Invoke(method, packet)));
        Assert.Equal(0, scope.BlockEntity.Calls);
        Assert.Equal(0, scope.Entity.Calls);
        Assert.Equal(0, scope.Fixture.Events.WorldAccessCalls);
        Assert.Equal(7, scope.Fixture.Hotbar[0].StackSize);
    }

    [Fact]
    public void PayloadTargetAndClaimedClientIdentityCannotChooseVanillaActor()
    {
        using var scope = new WorldGuardScope();
        scope.Fixture.Client.SentPlayerUid = "target-vanilla";
        scope.Entity.WatchedAttributes.SetString("playerUID", "target-vanilla");
        string? resolvedUid = null;
        NativeInventoryPacketGuardPatch.CustodyResolver = uid =>
        {
            resolvedUid = uid;
            return new(uid == "target-vanilla" ? PlayerItemCustodyOwner.Vanilla : PlayerItemCustodyOwner.Canonical, false);
        };
        var packet = new Packet_Client
        {
            EntityPacket = new Packet_EntityPacket { EntityId = scope.Entity.EntityId, Packetid = 41, Data = new byte[] { 3 } }
        };

        scope.InvokeRegistered(31, packet);

        Assert.Equal("player", resolvedUid);
        Assert.Equal(0, scope.Entity.Calls);
        Assert.Equal(11, scope.Entity.ExternalQuantity);
    }

    [Fact]
    public void RegisteredDelegateReevaluatesCustodyAcrossPhaseChanges()
    {
        using var scope = new WorldGuardScope();
        var owner = PlayerItemCustodyOwner.Canonical;
        NativeInventoryPacketGuardPatch.CustodyResolver = _ => new(owner, false);
        var packet = new Packet_Client
        {
            EntityPacket = new Packet_EntityPacket { EntityId = scope.Entity.EntityId, Packetid = 41, Data = new byte[] { 3 } }
        };

        scope.InvokeRegistered(31, packet);
        owner = PlayerItemCustodyOwner.Vanilla;
        scope.InvokeRegistered(31, packet);
        owner = PlayerItemCustodyOwner.Recovery;
        scope.InvokeRegistered(31, packet);

        Assert.Equal(1, scope.Entity.Calls);
        Assert.Equal(10, scope.Entity.ExternalQuantity);
    }

    [Theory]
    [InlineData("HandleBlockEntityPacket", 22)]
    [InlineData("HandleEntityPacket", 31)]
    public void VanillaRegisteredDelegatesReachRealNativeTargetCallbacksWithConnectedActor(string name, int packetId)
    {
        using var scope = new WorldGuardScope();
        string? resolvedUid = null;
        NativeInventoryPacketGuardPatch.CustodyResolver = uid =>
        {
            resolvedUid = uid;
            return new(PlayerItemCustodyOwner.Vanilla, false);
        };
        Packet_Client packet = name == "HandleBlockEntityPacket"
            ? new() { BlockEntityPacket = new Packet_BlockEntityPacket { X = 0, Y = 0, Z = 0, Packetid = 37, Data = new byte[] { 1, 2 } } }
            : new() { EntityPacket = new Packet_EntityPacket { EntityId = scope.Entity.EntityId, Packetid = 41, Data = new byte[] { 3 } } };

        scope.InvokeRegistered(packetId, packet);

        Assert.Equal("player", resolvedUid);
        Assert.Equal(name == "HandleBlockEntityPacket" ? 1 : 0, scope.BlockEntity.Calls);
        Assert.Equal(name == "HandleEntityPacket" ? 1 : 0, scope.Entity.Calls);
        Assert.Equal(name == "HandleBlockEntityPacket" ? 10 : 11, scope.BlockEntity.ExternalQuantity);
        Assert.Equal(name == "HandleEntityPacket" ? 10 : 11, scope.Entity.ExternalQuantity);
        Assert.Same(scope.Fixture.Player, name == "HandleBlockEntityPacket" ? scope.BlockEntity.Actor : scope.Entity.Actor);
    }

    // These branch probes prove Vanilla reaches native permission and dispatch logic. The
    // headless fixture intentionally stops before a complete chunk mutation.
    [Theory]
    [InlineData(0, "break")]
    [InlineData(1, "place")]
    [InlineData(2, "decor")]
    public void VanillaBlockPacketReachesObservableNativeBranch(int mode, string expectedBranch)
    {
        using var scope = new WorldGuardScope();
        NativeInventoryPacketGuardPatch.CustodyResolver = _ => new(PlayerItemCustodyOwner.Vanilla, false);
        if (mode == 1) scope.Fixture.Hotbar[0].Itemstack = new ItemStack(scope.PlaceBlock, 7);
        var packet = new Packet_Client
        {
            BlockPlaceOrBreak = new Packet_ClientBlockPlaceOrBreak
            {
                X = 0, Y = 0, Z = 0, OnBlockFace = 0, Mode = mode
            }
        };

        var invocation = Assert.IsType<TargetInvocationException>(Record.Exception(() => scope.InvokeRegistered(3, packet)));
        var error = Assert.IsType<WorldGuardScope.NativeBranchReached>(invocation.InnerException);

        Assert.Equal(expectedBranch, error.Branch);
        Assert.Equal(1, scope.Fixture.Events.WorldAccessCalls);
        Assert.Equal(7, scope.Fixture.Hotbar[0].StackSize);
    }

    [Theory]
    [InlineData("HandleBlockPlaceOrBreak")]
    [InlineData("HandleBlockEntityPacket")]
    [InlineData("HandleEntityPacket")]
    public void NewNativeBoundaryRemovalOrConflictWithdrawsReadiness(string name)
    {
        using var scope = new WorldGuardScope();
        NativeInventoryPacketGuardPatch.CustodyResolver = _ => PlayerItemCustodySnapshot.Unresolved;
        Assert.True(NativeInventoryPacketGuardPatch.IsReady);
        MethodInfo target = scope.Target(name);
        output.WriteLine(target.DeclaringType + "." + target.Name + " " + target.Module.ModuleVersionId + " "
            + Convert.ToHexString(SHA256.HashData(target.GetMethodBody()!.GetILAsByteArray()!)));
        if (name == "HandleEntityPacket")
            scope.Harmony.Patch(target, postfix: new HarmonyMethod(typeof(NativeWorldItemPacketGuardTests), nameof(ConflictPostfix)));
        else
            scope.Harmony.Unpatch(target, HarmonyPatchType.Prefix, scope.Harmony.Id);
        Assert.False(NativeInventoryPacketGuardPatch.IsReady);
    }

    [Theory]
    [InlineData("block-constructor")]
    [InlineData("server-registration")]
    public void NewRoutingWitnessConflictWithdrawsReadiness(string witness)
    {
        using var scope = new WorldGuardScope();
        NativeInventoryPacketGuardPatch.CustodyResolver = _ => PlayerItemCustodySnapshot.Unresolved;
        Assert.True(NativeInventoryPacketGuardPatch.IsReady);
        MethodBase route = witness == "block-constructor"
            ? AccessTools.Constructor(scope.BlockType, new[] { typeof(ServerMain) })
            : AccessTools.Method(typeof(ServerMain), "InitBasicPacketHandlers");
        output.WriteLine(route.DeclaringType + "." + route.Name + " " + route.Module.ModuleVersionId + " "
            + Convert.ToHexString(SHA256.HashData(route.GetMethodBody()!.GetILAsByteArray()!)));
        scope.Harmony.Patch(route, postfix: new HarmonyMethod(typeof(NativeWorldItemPacketGuardTests), nameof(ConflictPostfix)));
        Assert.False(NativeInventoryPacketGuardPatch.IsReady);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-owner")]
    [InlineData("missing-registration")]
    public void UnsupportedNewBoundaryCannotLeaveOldPacketPrefixesInstalled(string failure)
    {
        using var scope = new WorldGuardScope();
        NativeInventoryPacketGuardPatch.Reset();
        NativeInventoryPacketGuardPatch.Patch(scope.Harmony, WorldGuardScope.Logger,
            (owner, name, args) => failure == "missing-registration" && name == "InitBasicPacketHandlers" ? null
                : name == "HandleEntityPacket" && failure != "missing-registration"
                ? failure == "missing" ? null : AccessTools.Method(scope.BlockType, "HandleBlockEntityPacket", args)
                : AccessTools.Method(owner, name, args));
        Assert.False(NativeInventoryPacketGuardPatch.IsInstalled);
        Assert.DoesNotContain(Harmony.GetAllPatchedMethods(), method => Harmony.GetPatchInfo(method)?.Prefixes.Any(
            patch => patch.owner == scope.Harmony.Id && patch.PatchMethod.DeclaringType == typeof(NativeInventoryPacketGuardPatch)) == true);
    }

    [Fact]
    public void ResetAndReinstallRetainEarlierPacketAndTickGuardCoexistence()
    {
        using var scope = new WorldGuardScope();
        NativeInventoryPacketGuardPatch.CustodyResolver = _ => PlayerItemCustodySnapshot.Unresolved;
        Assert.True(NativeInventoryPacketGuardPatch.IsReady);
        NativeInventoryPacketGuardPatch.Reset();
        Assert.False(NativeInventoryPacketGuardPatch.IsInstalled);
        NativeInventoryPacketGuardPatch.Patch(scope.Harmony, WorldGuardScope.Logger);
        Assert.True(NativeInventoryPacketGuardPatch.IsInstalled);
        Assert.False(NativeInventoryPacketGuardPatch.IsReady);
        Assert.True(NativeInventoryTickGuardPatch.IsInstalled);
    }

    private static void ConflictPostfix() { }

    private sealed class WorldGuardScope : IDisposable
    {
        public sealed class NativeBranchReached(string branch) : Exception
        {
            public string Branch { get; } = branch;
        }

        public static ILogger Logger => PlayerDefenseServiceTests.Proxy.Create<ILogger>((_, _) => null);
        public readonly Harmony Harmony = new("vrpg.tests.native-world-guard." + Guid.NewGuid());
        public readonly Type BlockType = AccessTools.TypeByName("Vintagestory.Server.ServerSystemBlockSimulation");
        public readonly object BlockSystem;
        public readonly NativeInventoryGuardFixture Fixture;
        public readonly ProbeBlockEntity BlockEntity = new();
        public readonly ProbeEntity Entity = new() { EntityId = 73 };
        public readonly Block PlaceBlock = new() { BlockId = 0, Code = new("game:place-probe") };
        private readonly Array handlers;

        public WorldGuardScope(string uid = "player")
        {
            object seed = RuntimeHelpers.GetUninitializedObject(BlockType);
            Fixture = new NativeInventoryGuardFixture(seed, uid, worldAccess: true);
            var packetField = AccessTools.Field(typeof(ServerMain), "PacketHandlers");
            handlers = Array.CreateInstance(packetField.FieldType.GetElementType()!, 255);
            packetField.SetValue(Fixture.Server, handlers);
            var connectingField = AccessTools.Field(typeof(ServerMain), "PacketHandlingOnConnectingAllowed");
            connectingField.SetValue(Fixture.Server, Array.CreateInstance(connectingField.FieldType.GetElementType()!, 255));
            int oldThread = RuntimeEnv.ServerMainThreadId;
            RuntimeEnv.ServerMainThreadId = Environment.CurrentManagedThreadId;
            try { BlockSystem = AccessTools.Constructor(BlockType, new[] { typeof(ServerMain) }).Invoke(new object[] { Fixture.Server }); }
            finally { RuntimeEnv.ServerMainThreadId = oldThread; }
            AccessTools.Method(typeof(ServerMain), "InitBasicPacketHandlers").Invoke(Fixture.Server, Array.Empty<object>());
            ConfigureTargets();
            NativeInventoryPacketGuardPatch.Patch(Harmony, Logger);
            NativeInventoryPacketGuardPatch.CursorRecoveryObserver = _ => { };
            NativeInventoryTickGuardPatch.CustodyResolver = _ => PlayerItemCustodySnapshot.Unresolved;
            NativeInventoryTickGuardPatch.Patch(Harmony, Logger);
        }

        private void ConfigureTargets()
        {
            AccessTools.Field(typeof(ServerMain), "loadedChunksLock").SetValue(Fixture.Server, new FastRWLock(Fixture.Server));
            var chunks = new Dictionary<long, ServerChunk>();
            var chunk = (ServerChunk)RuntimeHelpers.GetUninitializedObject(typeof(ServerChunk));
            chunk.BlockEntities = new Dictionary<BlockPos, BlockEntity> { [new BlockPos(0, 0, 0)] = BlockEntity };
            chunks[0] = chunk;
            AccessTools.Field(typeof(ServerMain), "loadedChunks").SetValue(Fixture.Server, chunks);
            var map = (ServerWorldMap)RuntimeHelpers.GetUninitializedObject(typeof(ServerWorldMap));
            AccessTools.Field(typeof(ServerWorldMap), "server").SetValue(map, Fixture.Server);
            map.LandClaimByRegion = new Dictionary<long, List<LandClaim>>();
            map.index3dMulX = 1;
            map.index3dMulZ = 1;
            Fixture.Server.WorldMap = map;
            var branchAccessor = PlayerDefenseServiceTests.Proxy.Create<IBlockAccessor>((name, _) => name switch
            {
                "GetChunkAtBlockPos" => throw new NativeBranchReached("decor"),
                "GetBlock" => new Block { BlockId = 0, Code = new("game:branch-probe") },
                _ => null
            });
            map.RelaxedBlockAccess = branchAccessor;
            map.RawRelaxedBlockAccess = PlayerDefenseServiceTests.Proxy.Create<IBlockAccessor>((name, _) =>
                name == "GetBlock" ? throw new NativeBranchReached("place") : null);
            Fixture.Server.Blocks = new List<Block> { PlaceBlock };
            Fixture.FixturePlayerForWorldAccess();
            AccessTools.Field(typeof(ServerMain), "serverConsoleId").SetValue(Fixture.Server, Fixture.Player.ClientId);
            Fixture.Events.ObserveWorldAccess = true;
            Fixture.Events.BreakBranch = () => throw new NativeBranchReached("break");
            var api = (ServerCoreAPI)RuntimeHelpers.GetUninitializedObject(typeof(ServerCoreAPI));
            AccessTools.Field(typeof(ServerCoreAPI), "server").SetValue(api, Fixture.Server);
            Type eventType = AccessTools.TypeByName("Vintagestory.Server.ServerEventAPI");
            AccessTools.Field(typeof(ServerCoreAPI), "eventapi").SetValue(api, RuntimeHelpers.GetUninitializedObject(eventType));
            AccessTools.Field(typeof(ServerMain), "api").SetValue(Fixture.Server, api);
            var entities = new CachingConcurrentDictionary<long, Entity>();
            entities[Entity.EntityId] = Entity;
            AccessTools.Field(typeof(ServerMain), "LoadedEntities").SetValue(Fixture.Server, entities);
        }

        public MethodInfo Target(string name) => AccessTools.Method(
            name == "HandleEntityPacket" ? typeof(ServerMain) : BlockType, name,
            new[] { typeof(Packet_Client), typeof(ConnectedClient) });

        public void Invoke(string name, Packet_Client packet) => Target(name).Invoke(
            name == "HandleEntityPacket" ? Fixture.Server : BlockSystem, new object[] { packet, Fixture.Client });

        public void InvokeRegistered(int id, Packet_Client packet) =>
            ((Delegate)handlers.GetValue(id)!).DynamicInvoke(packet, Fixture.Client);

        public void Dispose()
        {
            Harmony.UnpatchAll(Harmony.Id);
            NativeInventoryPacketGuardPatch.Reset();
            NativeInventoryTickGuardPatch.Reset();
        }
    }

    private sealed class ProbeBlockEntity : BlockEntity
    {
        public int Calls;
        public int ExternalQuantity = 11;
        public IPlayer? Actor;
        public override void OnReceivedClientPacket(IPlayer fromPlayer, int packetid, byte[] data)
        {
            Calls++;
            ExternalQuantity--;
            Actor = fromPlayer;
        }
    }

    private sealed class ProbeEntity : Entity
    {
        public int Calls;
        public int ExternalQuantity = 11;
        public IServerPlayer? Actor;
        public override void OnReceivedClientPacket(IServerPlayer player, int packetId, byte[] packetData)
        {
            Calls++;
            ExternalQuantity--;
            Actor = player;
        }
    }
}
