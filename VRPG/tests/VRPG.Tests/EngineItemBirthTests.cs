using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using VRPG.Inventory;
using VRPG.Inventory.Bridges;
using VRPG.Inventory.Persistence;
using VRPG.Patches;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.API.Datastructures;
using Vintagestory.Server;
using Xunit;

namespace VRPG.Tests;

[Collection("EngineItemBirth")]
public sealed class EngineItemBirthTests
{
    [Theory]
    [InlineData(PlayerItemCustodyOwner.Recovery)]
    [InlineData(PlayerItemCustodyOwner.Canonical)]
    public void ReadOnlyCustodyRefusesPickupWithoutCreditOrSourceRemoval(PlayerItemCustodyOwner owner)
    {
        using var scope = new BirthScope();
        var entity = ItemEntity();
        scope.BeforePublication(entity);
        EngineItemFlowPatch.CoordinatorResolver = () => scope.Bridge;
        EngineItemFlowPatch.CustodyResolver = _ => new(owner, false);
        var player = new EntityPlayer();
        player.WatchedAttributes.SetString("playerUID", "player");
        bool result = true;
        Assert.False(EngineItemFlowPatch.OnFoundCollectiblePrefix(
            new Vintagestory.GameContent.EntityBehaviorCollectEntities(player), entity, ref result));
        Assert.False(result);
        Assert.NotNull(entity.Itemstack);
        Assert.Equal(0, scope.Total());
    }

    [Fact]
    public void ReattachingCachedObserverDoesNotReviveOldFreshLease()
    {
        using var scope = new BirthScope();
        var entity = ItemEntity();
        scope.BeforePublication(entity);
        var observer = EngineItemFlowPatch.BeforePublicationObserver;
        EngineItemFlowPatch.BeforePublicationObserver = null;
        EngineItemFlowPatch.BeforePublicationObserver = observer;
        Assert.False(new EngineItemPickupSource(entity).HasPrepublicationIdentity);
        Assert.Equal(PlayerItemBridgeStage.Refused, scope.Bridge.Pickup("player", new EngineItemPickupSource(entity)).Stage);
        Assert.NotNull(entity.Itemstack);
        var next = ItemEntity();
        scope.BeforePublication(next);
        Assert.True(new EngineItemPickupSource(next).HasPrepublicationIdentity);
    }

    [Fact]
    public void InstalledNativeSpawnHookEstablishesFreshIdentityBeforeInitialization()
    {
        using var scope = new BirthScope();
        var spawn = AccessTools.Method(typeof(ServerMain), "SpawnEntity_internal");
        Assert.Contains(Harmony.GetPatchInfo(spawn)?.Prefixes.AsEnumerable() ?? Array.Empty<Patch>(), patch => patch.owner == scope.Harmony.Id);
        var entity = ItemEntity();
        // Running the installed prefix at the entry boundary does not require a joined server.
        foreach (var prefix in Harmony.GetPatchInfo(spawn)!.Prefixes.Where(p => p.owner == scope.Harmony.Id))
            prefix.PatchMethod.Invoke(null, new object[] { entity });
        string token = new EngineItemPickupSource(entity).Token;
        Assert.StartsWith("v1:", token);
        Assert.True(Guid.TryParseExact(token[3..], "N", out _));
        entity.EntityId = 75; // Native collision repair may assign a different ID later.
        EngineItemPickupSource.Observe(entity, loaded: false);
        EngineItemPickupSource.Observe(entity, loaded: true);
        Assert.Equal(token, new EngineItemPickupSource(entity).Token);
        Assert.False(new EngineItemPickupSource(entity).HasPersistedIdentity);
        Assert.Equal(PlayerItemBridgeStage.Completed, scope.Bridge.Pickup("player", new EngineItemPickupSource(entity)).Stage);
        Assert.Null(entity.Itemstack);
        Assert.Equal(3, scope.Total());
    }

    [Fact]
    public void FreshPickupRestoredFromEngineBytesIsSuppressedWithoutSecondCredit()
    {
        using var scope = new BirthScope();
        var entity = ItemEntity();
        scope.BeforePublication(entity);
        byte[] saved = Serialize(entity);
        Assert.Equal(PlayerItemBridgeStage.Completed, scope.Bridge.Pickup("player", new EngineItemPickupSource(entity)).Stage);
        scope.Restart();
        var restored = Restore(saved);
        EngineItemPickupSource.Observe(restored, loaded: true);
        Assert.True(new EngineItemPickupSource(restored).HasPersistedIdentity);
        Assert.True(scope.Bridge.SuppressClaimedPickup(new EngineItemPickupSource(restored)));
        Assert.Null(restored.Itemstack);
        Assert.Equal(3, scope.Total());
    }

    [Theory]
    [InlineData("quantity")]
    [InlineData("payload")]
    [InlineData("token")]
    [InlineData("digest")]
    [InlineData("missing-digest")]
    [InlineData("missing-proof")]
    public void ChangedBirthProofCannotBeRenewedByObserversOrReload(string mutation)
    {
        using var scope = new BirthScope();
        var entity = ItemEntity();
        scope.BeforePublication(entity);
        if (mutation == "quantity") entity.Itemstack.StackSize = 4;
        if (mutation == "payload") entity.Itemstack.Attributes.SetString("state", "changed");
        if (mutation == "token") entity.WatchedAttributes.SetString(EngineItemPickupSource.BirthTokenAttribute, Guid.NewGuid().ToString("N"));
        if (mutation == "digest") entity.WatchedAttributes.SetString("vrpg:item-birth-snapshot", "invalid");
        if (mutation == "missing-digest") entity.WatchedAttributes.RemoveAttribute("vrpg:item-birth-snapshot");
        if (mutation == "missing-proof")
        {
            entity.WatchedAttributes.RemoveAttribute("vrpg:item-birth-snapshot");
            entity.WatchedAttributes.RemoveAttribute("vrpg:item-birth-format");
        }
        EngineItemPickupSource.Observe(entity, loaded: false);
        EngineItemPickupSource.Observe(entity, loaded: true);
        Assert.Equal(PlayerItemBridgeStage.Refused, scope.Bridge.Pickup("player", new EngineItemPickupSource(entity)).Stage);
        var restored = Restore(Serialize(entity));
        EngineItemPickupSource.Observe(restored, loaded: true);
        Assert.Equal(PlayerItemBridgeStage.Refused, scope.Bridge.Pickup("player", new EngineItemPickupSource(restored)).Stage);
        Assert.Equal(0, scope.Total());
        Assert.NotNull(restored.Itemstack);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    public void FreshPickupWriteFailuresKeepClaimBeforeRemovalAndRetryTotals(int failedWrite, bool writeThenThrow)
    {
        using var scope = new BirthScope();
        var entity = ItemEntity();
        scope.BeforePublication(entity);
        byte[] saved = Serialize(entity);
        scope.Storage.FailAt = failedWrite;
        scope.Storage.WriteThenThrow = writeThenThrow;
        var receipt = scope.Bridge.Pickup("player", new EngineItemPickupSource(entity));
        Assert.NotEqual(PlayerItemBridgeStage.Completed, receipt.Stage);
        Assert.Equal(failedWrite == 1, entity.Itemstack != null);
        scope.Storage.FailAt = 0;
        if (failedWrite == 1)
            Assert.Equal(PlayerItemBridgeStage.Completed, scope.Bridge.Pickup("player", new EngineItemPickupSource(entity)).Stage);
        scope.Restart();
        scope.Bridge.Reconcile();
        var restored = Restore(saved);
        EngineItemPickupSource.Observe(restored, loaded: true);
        Assert.True(scope.Bridge.SuppressClaimedPickup(new EngineItemPickupSource(restored)));
        Assert.Equal(3, scope.Total());
    }

    [Fact]
    public void UnrelatedFreshItemsKeepSeparateIdentityEvenWhenEntityIdsAreReused()
    {
        using var scope = new BirthScope();
        var first = ItemEntity();
        var second = ItemEntity();
        scope.BeforePublication(first);
        scope.BeforePublication(second);
        first.EntityId = second.EntityId = 42;
        Assert.NotEqual(new EngineItemPickupSource(first).Token, new EngineItemPickupSource(second).Token);
        Assert.Equal(PlayerItemBridgeStage.Completed, scope.Bridge.Pickup("player", new EngineItemPickupSource(first)).Stage);
        Assert.False(scope.Bridge.SuppressClaimedPickup(new EngineItemPickupSource(second)));
        Assert.Equal(PlayerItemBridgeStage.Completed, scope.Bridge.Pickup("player", new EngineItemPickupSource(second)).Stage);
        Assert.Equal(6, scope.Total());
    }

    [Fact]
    public void LateSpawnObservationCannotLaterPretendToBeThePrepublicationHook()
    {
        using var scope = new BirthScope();
        var entity = ItemEntity();
        EngineItemPickupSource.Observe(entity, loaded: false);
        scope.BeforePublication(entity);
        Assert.Equal(PlayerItemBridgeStage.Refused, scope.Bridge.Pickup("player", new EngineItemPickupSource(entity)).Stage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealNativeInitializationPreservesBirthEvidenceButChangedCallbackPayloadRefuses(bool mutate)
    {
        using var scope = new BirthScope();
        var entity = ItemEntity();
        scope.BeforePublication(entity);
        string token = new EngineItemPickupSource(entity).Token;
        string digest = entity.WatchedAttributes.GetString("vrpg:item-birth-snapshot");
        byte[]? earlySerialization = null;
        entity.OnInitialized += () =>
        {
            earlySerialization = Serialize(entity);
            if (mutate) entity.Itemstack.StackSize++;
        };
        var blocks = PlayerDefenseServiceTests.Proxy.Create<IBlockAccessor>((method, _) => method switch
        {
            "GetBlock" => new Block(),
            "GetDistanceToRainFall" => 0,
            _ => null
        });
        var item = entity.Itemstack.Item;
        var world = PlayerDefenseServiceTests.Proxy.Create<IServerWorldAccessor>((method, _) => method switch
        {
            "get_Side" => EnumAppSide.Server,
            "get_AllOnlinePlayers" => Array.Empty<IPlayer>(),
            "get_ElapsedMilliseconds" => 0L,
            "get_BlockAccessor" => blocks,
            "GetItem" => item,
            _ => null
        });
        var api = PlayerDefenseServiceTests.Proxy.Create<ICoreServerAPI>((method, _) => method switch
        {
            "get_Side" => EnumAppSide.Server,
            "get_World" => world,
            _ => null
        });
        var properties = new EntityProperties { Server = new EntityServerProperties(Array.Empty<JsonObject>(), new()), CollisionBoxSize = null, SelectionBoxSize = null };
        entity.Initialize(properties, api, 0);
        entity.AfterInitialized(true);
        entity.EntityId = 99;
        EngineItemPickupSource.Observe(entity, loaded: false);
        Assert.Equal(token, new EngineItemPickupSource(entity).Token);
        Assert.Equal(digest, entity.WatchedAttributes.GetString("vrpg:item-birth-snapshot"));
        Assert.NotNull(earlySerialization);
        var savedDuringInitialization = Restore(earlySerialization!);
        Assert.Equal(token, new EngineItemPickupSource(savedDuringInitialization).Token);
        Assert.Equal(mutate ? PlayerItemBridgeStage.Refused : PlayerItemBridgeStage.Completed,
            scope.Bridge.Pickup("player", new EngineItemPickupSource(entity)).Stage);
        Assert.Equal(mutate ? 0 : 3, scope.Total());
    }

    [Theory]
    [InlineData("SpawnEntity_internal")]
    [InlineData("Initialize")]
    [InlineData("AfterInitialized")]
    [InlineData("ToBytes")]
    [InlineData("FromBytes")]
    [InlineData("wrong-body")]
    public void MissingOrMismatchedNativeHookCannotIssueFreshEvidence(string missing)
    {
        var logs = new List<string>();
        using var scope = new BirthScope(install: false);
        EngineItemFlowPatch.Patch(scope.Harmony, PlayerDefenseServiceTests.Proxy.Create<ILogger>((method, args) =>
        {
            if (method == "Error") logs.Add((string)args![0]!);
            return null;
        }), (type, name, parameters) => name == missing ? null : missing == "wrong-body"
            ? AccessTools.Method(typeof(EngineItemBirthTests), nameof(NoopPrefix)) : AccessTools.Method(type, name, parameters));
        var entity = ItemEntity();
        scope.BeforePublication(entity);
        Assert.False(EngineItemFlowPatch.IsFreshIdentityInstalled);
        Assert.Equal("", new EngineItemPickupSource(entity).Token);
        Assert.Equal(PlayerItemBridgeStage.Refused, scope.Bridge.Pickup("player", new EngineItemPickupSource(entity)).Stage);
        Assert.NotEmpty(logs);
    }

    [Theory]
    [InlineData("unpatch")]
    [InlineData("reset")]
    [InlineData("conflicting-patch")]
    [InlineData("dispose-module")]
    public void LostHookOrObserverReadinessRevokesExistingFreshEvidence(string failure)
    {
        using var scope = new BirthScope();
        var entity = ItemEntity();
        scope.BeforePublication(entity);
        Assert.True(EngineItemFlowPatch.IsFreshIdentityInstalled);
        var foreign = new Harmony("vrpg.tests.birth.foreign");
        try
        {
            if (failure == "unpatch") scope.Harmony.UnpatchAll(scope.Harmony.Id);
            if (failure == "reset") EngineItemFlowPatch.Reset();
            if (failure == "conflicting-patch") foreign.Patch(AccessTools.Method(typeof(EntityItem), nameof(EntityItem.Initialize)),
                prefix: new HarmonyMethod(typeof(EngineItemBirthTests), nameof(NoopPrefix)));
            if (failure == "dispose-module") new PlayerItemPersistenceModule().Dispose();
            Assert.False(EngineItemFlowPatch.IsFreshIdentityInstalled);
            Assert.False(new EngineItemPickupSource(entity).HasPrepublicationIdentity);
            Assert.Equal(PlayerItemBridgeStage.Refused, scope.Bridge.Pickup("player", new EngineItemPickupSource(entity)).Stage);
            Assert.Equal(3, entity.Itemstack.StackSize);
        }
        finally { foreign.UnpatchAll(foreign.Id); }
    }

    private static void NoopPrefix() { }

    [Fact]
    public void ObserverFailureRevokesEvenPartiallyIssuedEvidenceWithoutFailingNativeSpawn()
    {
        using var scope = new BirthScope();
        var entity = ItemEntity();
        EngineItemFlowPatch.BeforePublicationObserver = (source, ready) =>
        {
            EngineItemPickupSource.EstablishBeforePublication(source, ready);
            throw new IOException("Observer failed after assigning identity.");
        };
        Assert.Null(Record.Exception(() => scope.BeforePublication(entity)));
        Assert.False(EngineItemFlowPatch.IsFreshIdentityInstalled);
        Assert.False(new EngineItemPickupSource(entity).HasPrepublicationIdentity);
        Assert.Equal(PlayerItemBridgeStage.Refused, scope.Bridge.Pickup("player", new EngineItemPickupSource(entity)).Stage);
    }

    [Fact]
    public void NativePartialStackMergeCannotRefreshEitherBirthAcrossSaveLoad()
    {
        using var scope = new BirthScope();
        var donor = ItemEntity();
        var recipient = ItemEntity();
        var world = PlayerDefenseServiceTests.Proxy.Create<IWorldAccessor>((method, _) => method == "get_Side" ? EnumAppSide.Server : null);
        var api = PlayerDefenseServiceTests.Proxy.Create<ICoreAPI>((method, _) => method == "get_World" ? world : null);
        AccessTools.Field(typeof(CollectibleObject), "api").SetValue(donor.Itemstack.Collectible, api);
        AccessTools.Field(typeof(CollectibleObject), "api").SetValue(recipient.Itemstack.Collectible, api);
        scope.BeforePublication(donor);
        scope.BeforePublication(recipient);
        Assert.Equal(1, donor.Slot.TryPutInto(world, recipient.Slot, 1));
        Assert.Equal(2, donor.Itemstack.StackSize);
        Assert.Equal(4, recipient.Itemstack.StackSize);
        foreach (var entity in new[] { donor, recipient })
        {
            Assert.False(new EngineItemPickupSource(entity).HasPrepublicationIdentity);
            var restored = Restore(Serialize(entity));
            EngineItemPickupSource.Observe(restored, loaded: true);
            Assert.Equal(PlayerItemBridgeStage.Refused, scope.Bridge.Pickup("player", new EngineItemPickupSource(restored)).Stage);
        }
        Assert.Equal(0, scope.Total());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovalInterruptionKeepsDurableClaimAndRestoredSourceSuppressible(bool afterRemoval)
    {
        using var scope = new BirthScope();
        var entity = ItemEntity();
        scope.BeforePublication(entity);
        byte[] saved = Serialize(entity);
        Assert.Equal(PlayerItemBridgeStage.ReconcileRequired,
            scope.Bridge.Pickup("player", new InterruptedRemoval(new EngineItemPickupSource(entity), afterRemoval)).Stage);
        Assert.Equal(!afterRemoval, entity.Itemstack != null);
        scope.Restart();
        scope.Bridge.Reconcile();
        var restored = Restore(saved);
        EngineItemPickupSource.Observe(restored, loaded: true);
        Assert.True(scope.Bridge.SuppressClaimedPickup(new EngineItemPickupSource(restored)));
        Assert.Equal(3, scope.Total());
    }

    [Theory]
    [InlineData("subclass")]
    [InlineData("callback")]
    [InlineData("temperature")]
    [InlineData("transition")]
    public void UnsupportedSourceAndEvolvingContentsRemainUntouchedAtNativePickupBoundary(string kind)
    {
        using var scope = new BirthScope();
        EntityItem entity = kind == "subclass" ? new CustomEntityItem { Itemstack = ItemEntity().Itemstack } : ItemEntity();
        var callback = new CallbackItem { Code = new AssetLocation("game:stone"), MaxStackSize = 64 };
        if (kind == "callback") entity.Itemstack = new ItemStack(callback, 3);
        if (kind == "temperature") entity.Itemstack.Attributes["temperature"] = new TreeAttribute();
        if (kind == "transition") entity.Itemstack.Attributes["transitionstate"] = new TreeAttribute();
        scope.BeforePublication(entity);
        EngineItemFlowPatch.CoordinatorResolver = () => scope.Bridge;
        EngineItemFlowPatch.CustodyResolver = _ => new(PlayerItemCustodyOwner.Canonical, true);
        var player = new EntityPlayer();
        player.WatchedAttributes.SetString("playerUID", "player");
        bool collected = true;
        Assert.False(EngineItemFlowPatch.OnFoundCollectiblePrefix(new Vintagestory.GameContent.EntityBehaviorCollectEntities(player), entity, ref collected));
        Assert.False(collected);
        Assert.Equal(3, entity.Itemstack.StackSize);
        Assert.Equal(0, callback.Calls);
        Assert.Equal(0, scope.Total());
    }

    [Fact]
    public void TombstoneAlsoSuppressesRestoredFreshSourceForUnownedCollector()
    {
        using var scope = new BirthScope();
        var entity = ItemEntity();
        scope.BeforePublication(entity);
        entity.WatchedAttributes.SetString("othermod:extension", "keep");
        byte[] saved = Serialize(entity);
        Assert.Equal(PlayerItemBridgeStage.Completed, scope.Bridge.Pickup("player", new EngineItemPickupSource(entity)).Stage);
        var restored = Restore(saved);
        EngineItemPickupSource.Observe(restored, loaded: true);
        EngineItemFlowPatch.CoordinatorResolver = () => scope.Bridge;
        EngineItemFlowPatch.CustodyResolver = _ => new(PlayerItemCustodyOwner.Vanilla, false);
        bool result = false;
        Assert.False(EngineItemFlowPatch.OnFoundCollectiblePrefix(new Vintagestory.GameContent.EntityBehaviorCollectEntities(new EntityPlayer()), restored, ref result));
        Assert.True(result);
        Assert.Null(restored.Itemstack);
        Assert.Equal("keep", restored.WatchedAttributes.GetString("othermod:extension"));
        Assert.Equal(3, scope.Total());
    }

    private sealed class CustomEntityItem : EntityItem;
    private sealed class CallbackItem : Item
    {
        public int Calls;
        public override void OnCollected(ItemStack stack, Entity byEntity) => Calls++;
    }
    private sealed class InterruptedRemoval(EngineItemPickupSource source, bool afterRemoval) : IEngineItemPickupSource
    {
        public string Token => source.Token;
        public bool HasPersistedIdentity => source.HasPersistedIdentity;
        public bool HasPrepublicationIdentity => source.HasPrepublicationIdentity;
        public ItemStack? Stack => source.Stack;
        public void Remove()
        {
            if (afterRemoval) source.Remove();
            throw new IOException("Injected removal interruption.");
        }
    }

    [Fact]
    public void SpawnObserverPreservesLoadedTokenAndItsOriginalSnapshot()
    {
        var entity = ItemEntity();
        string token = Guid.NewGuid().ToString("N");
        entity.WatchedAttributes.SetString(EngineItemPickupSource.BirthTokenAttribute, token);
        EngineItemPickupSource.Observe(entity, loaded: true);
        EngineItemPickupSource.Observe(entity, loaded: false);
        Assert.Equal(token, new EngineItemPickupSource(entity).Token);
        Assert.True(new EngineItemPickupSource(entity).HasPersistedIdentity);
    }

    [Fact]
    public void RepeatedLoadObserverCannotRefreshChangedQuantityProof()
    {
        var entity = ItemEntity();
        entity.WatchedAttributes.SetString(EngineItemPickupSource.BirthTokenAttribute, Guid.NewGuid().ToString("N"));
        EngineItemPickupSource.Observe(entity, loaded: true);
        entity.Itemstack.StackSize = 4;
        EngineItemPickupSource.Observe(entity, loaded: true);
        Assert.False(new EngineItemPickupSource(entity).HasPersistedIdentity);
    }

    [Fact]
    public void SlotProofCannotAuthorizeDifferentSerializedWatchedStack()
    {
        var entity = ItemEntity();
        entity.WatchedAttributes.SetString(EngineItemPickupSource.BirthTokenAttribute, Guid.NewGuid().ToString("N"));
        EngineItemPickupSource.Observe(entity, loaded: true);
        entity.WatchedAttributes.SetItemstack("itemstack", new ItemStack(entity.Itemstack.Item, 9));
        Assert.False(new EngineItemPickupSource(entity).HasPersistedIdentity);
    }

    private static EntityItem ItemEntity() => new()
    {
        Code = new AssetLocation("game:item"),
        Itemstack = new ItemStack(new Item { Code = new AssetLocation("game:stone"), MaxStackSize = 64 }, 3)
    };

    private static byte[] Serialize(EntityItem entity)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        entity.ToBytes(writer, false);
        return stream.ToArray();
    }

    private static EntityItem Restore(byte[] bytes)
    {
        var entity = new EntityItem();
        using var reader = new BinaryReader(new MemoryStream(bytes));
        entity.FromBytes(reader, false);
        var item = ItemEntity().Itemstack.Item;
        var world = PlayerDefenseServiceTests.Proxy.Create<IWorldAccessor>((name, _) => name == "GetItem" ? item : null);
        Assert.True(entity.Itemstack.ResolveBlockOrItem(world));
        return entity;
    }

    private sealed class BirthScope : IDisposable
    {
        public Harmony Harmony { get; } = new("vrpg.tests.birth." + Guid.NewGuid());
        public Storage Storage { get; } = new();
        public PlayerItemBridgeCoordinator Bridge { get; private set; } = null!;
        public BirthScope(bool install = true)
        {
            EngineItemFlowPatch.BeforePublicationObserver = EngineItemPickupSource.EstablishBeforePublication;
            if (install) EngineItemFlowPatch.Patch(Harmony, PlayerDefenseServiceTests.Proxy.Create<ILogger>((_, _) => null));
            Restart();
        }
        public void BeforePublication(EntityItem entity) => EngineItemFlowPatch.BeforeSpawnPrefix(entity);
        public void Restart()
        {
            var archive = new PlayerItemArchiveService(new PlayerItemArchiveRepository(Storage));
            Assert.True(archive.Load().Success);
            Bridge = new PlayerItemBridgeCoordinator(archive, new Resolver(ItemEntity().Itemstack.Item));
        }
        public long Total() => Bridge.Archive.Directory.ForPlayer("player").Query(new("", "", 0, 100)).Entries.Sum(e => e.Quantity);
        public void Dispose() { Harmony.UnpatchAll(Harmony.Id); EngineItemFlowPatch.Reset(); }
    }

    private sealed class Resolver(Item item) : ICollectibleResolver
    {
        public Item? GetItem(AssetLocation code) => code.Equals(item.Code) ? item : null;
        public Block? GetBlock(AssetLocation code) => null;
    }

    private sealed class Storage : IDurablePlayerItemArchiveStorage
    {
        public bool IsCrashDurable => true;
        private readonly Dictionary<string, byte[]> slots = new();
        private int writes;
        public int FailAt;
        public bool WriteThenThrow;
        public byte[]? Read(string key) => slots.TryGetValue(key, out var bytes) ? (byte[])bytes.Clone() : null;
        public void Write(string key, byte[] bytes)
        {
            bool fail = ++writes == FailAt;
            if (!fail || WriteThenThrow) slots[key] = (byte[])bytes.Clone();
            if (fail) throw new IOException("Injected claim persistence interruption.");
        }
    }
}

[CollectionDefinition("EngineItemBirth", DisableParallelization = true)]
public sealed class EngineItemBirthCollection;
