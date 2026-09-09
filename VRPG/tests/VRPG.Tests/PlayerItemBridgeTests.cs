using System;
using System.Collections.Generic;
using System.IO;
using VRPG.Inventory;
using VRPG.Inventory.Persistence;
using VRPG.Inventory.Bridges;
using Vintagestory.API.Common;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xunit;

namespace VRPG.Tests;

public sealed class PlayerItemBridgeTests
{
    [Theory]
    [InlineData("missing-newest")]
    [InlineData("empty-newest")]
    [InlineData("missing-older")]
    [InlineData("empty-slots")]
    [InlineData("stale-newest")]
    [InlineData("missing-format")]
    [InlineData("missing-generation")]
    [InlineData("missing-manifest")]
    [InlineData("cleared-slots-and-manifest")]
    public void EnvelopeSlotDamageCannotSelectOlderOrEmptyWritableCustody(string damage)
    {
        string root = Path.Combine(Path.GetTempPath(), "vrpg-slot-proof-" + Guid.NewGuid().ToString("N"));
        try
        {
            var durable = new WorldScopedPlayerItemArchiveStorage(root, "world", new MemoryStorage());
            var repository = new PlayerItemArchiveRepository(durable);
            Assert.True(repository.Save(new PlayerItemArchive()).Success);
            Assert.True(repository.Save(new PlayerItemArchive()).Success);
            var document = JObject.Parse(File.ReadAllText(durable.ArchivePath));
            var slots = (JObject)document["Slots"]!;
            if (damage == "missing-newest") slots.Remove(PlayerItemArchiveRepository.SlotBKey);
            if (damage == "empty-newest") slots[PlayerItemArchiveRepository.SlotBKey] = "";
            if (damage == "missing-older") slots.Remove(PlayerItemArchiveRepository.SlotAKey);
            if (damage == "empty-slots") slots.RemoveAll();
            if (damage == "stale-newest") slots[PlayerItemArchiveRepository.SlotBKey] = slots[PlayerItemArchiveRepository.SlotAKey]!.DeepClone();
            if (damage == "missing-format") document.Remove("Format");
            if (damage == "missing-generation") document.Remove("CommittedGeneration");
            if (damage == "missing-manifest") document.Remove("SlotDigests");
            if (damage == "cleared-slots-and-manifest")
            {
                slots.RemoveAll();
                ((JObject)document["SlotDigests"]!).RemoveAll();
            }
            File.WriteAllText(durable.ArchivePath, document.ToString());
            string damagedDocument = File.ReadAllText(durable.ArchivePath);
            var service = new PlayerItemArchiveService(repository);
            Assert.False(service.Load().Success);
            Assert.False(service.IsLoaded);
            Assert.False(service.SaveDirty().Success);
            Assert.Equal(damagedDocument, File.ReadAllText(durable.ArchivePath));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void CompletedClaimRewrittenAsPreparedCannotReinsertAfterRestart()
    {
        var storage = new MemoryStorage();
        var archive = Load(storage);
        var item = new Item { Code = new AssetLocation("game:stone"), MaxStackSize = 64 };
        var bridge = new PlayerItemBridgeCoordinator(archive, new Resolver(item));
        bridge.RegisterClaimProducer("reward", PlayerItemBridgeOperation.Reward);
        Assert.Equal(PlayerItemBridgeStage.Completed, bridge.RegisterClaim(new("claim", "player", PlayerItemBridgeOperation.Reward,
            "reward", "", 3, "objective", "canonical", 0), new ItemStack(item, 3)).Stage);
        var repository = new PlayerItemArchiveRepository(storage);
        var corrupt = repository.Load().Archive;
        corrupt.Bridges["claim"].Stage = PlayerItemBridgeStage.Prepared;
        Assert.True(repository.Save(corrupt).Success);
        var restarted = new PlayerItemArchiveService(repository);
        Assert.False(restarted.Load().Success);
        Assert.False(restarted.IsLoaded);
        Assert.Empty(new PlayerItemBridgeCoordinator(restarted, new Resolver(item)).Reconcile());
        Assert.Equal(3, repository.Load().Archive.Players["player"].Entries.Single().Quantity);
    }

    [Theory]
    [InlineData("empty-code")]
    [InlineData("kind")]
    [InlineData("state-policy")]
    [InlineData("unique-quantity")]
    public void MalformedPreparedClaimPrototypeRefusesDeserialization(string damage)
    {
        var prototype = new PlayerItemPrototype(PlayerItemStorageKind.Aggregate, "game:stone", "materials", "Stone", new byte[] { 0 });
        prototype = damage switch
        {
            "empty-code" => prototype with { AssetCode = "" },
            "kind" => prototype with { Kind = (PlayerItemStorageKind)999 },
            "state-policy" => prototype with { StatePolicy = (PlayerItemStatePolicy)999 },
            _ => prototype with { Kind = PlayerItemStorageKind.Unique }
        };
        var receipt = new PlayerItemBridgeReceipt
        {
            Request = new("claim", "player", PlayerItemBridgeOperation.Reward, "reward", "", 2, "objective", "canonical", 0),
            Stage = PlayerItemBridgeStage.Prepared, Claim = prototype
        };
        var serializer = new PlayerItemArchiveSerializer();
        Assert.False(serializer.TryDeserialize(serializer.Serialize(new PlayerItemArchive { Bridges = new() { ["claim"] = receipt } }), out _, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EqualRequestExtensionsRetryAcrossCloneAndRestartButConflictsRefuse(bool claim)
    {
        var storage = new MemoryStorage();
        var archive = Load(storage);
        var item = new Item { Code = new AssetLocation("game:stone"), MaxStackSize = 64 };
        var bridge = new PlayerItemBridgeCoordinator(archive, new Resolver(item));
        var store = archive.Directory.ForPlayer("player");
        var encoded = ItemStackCodec.Encode(new ItemStack(item, 2));
        var inserted = store.Insert("seed", new PlayerItemPrototype(PlayerItemStorageKind.Aggregate,
            "game:stone", "materials", "Stone", encoded.Payload.ExactState), 2, 0);
        int destination = 0;
        bridge.RegisterClaimProducer("producer", PlayerItemBridgeOperation.Reward);
        bridge.Register(new EngineItemBridgeAdapter("producer", PlayerItemBridgeOperation.Drop,
            (_, stack) => { destination += stack.StackSize; return new(true, 2, 0, "effect"); }, _ => new(false, 0, 0, "unknown")));
        var request = new PlayerItemBridgeRequest("extensions", "player", claim ? PlayerItemBridgeOperation.Reward : PlayerItemBridgeOperation.Drop,
            "producer", claim ? "" : inserted.EntryId, 2, "source", "destination", store.Revision)
        { ExtensionData = new Dictionary<string, JToken> { ["future"] = JObject.Parse("{\"a\":1,\"b\":[2,3]}") } };
        Assert.Equal(PlayerItemBridgeStage.Completed, claim ? bridge.RegisterClaim(request, new ItemStack(item, 2)).Stage : bridge.Execute(request).Stage);
        var clone = request with { ExtensionData = new Dictionary<string, JToken> { ["future"] = JObject.Parse("{\"b\":[2,3],\"a\":1}") } };
        Assert.Equal(PlayerItemBridgeStage.Completed, claim ? bridge.RegisterClaim(clone, new ItemStack(item, 2)).Stage : bridge.Execute(clone).Stage);
        bridge = new PlayerItemBridgeCoordinator(Load(storage), new Resolver(item));
        bridge.RegisterClaimProducer("producer", PlayerItemBridgeOperation.Reward);
        Assert.Equal(PlayerItemBridgeStage.Completed, claim ? bridge.RegisterClaim(clone, new ItemStack(item, 2)).Stage : bridge.Execute(clone).Stage);
        var conflict = clone with { ExtensionData = new Dictionary<string, JToken> { ["future"] = JObject.Parse("{\"a\":9,\"b\":[2,3]}") } };
        Assert.Equal(PlayerItemBridgeStage.Refused, claim ? bridge.RegisterClaim(conflict, new ItemStack(item, 2)).Stage : bridge.Execute(conflict).Stage);
        Assert.Equal(claim ? 0 : 2, destination);
        Assert.Equal(claim ? 4 : 0, bridge.Archive.Directory.ForPlayer("player").Query(new("", "", 0, 100)).Entries.Sum(e => e.Quantity));
    }
    [Theory]
    [InlineData(0, 2, PlayerItemBridgeStage.RolledBack, 0)]
    [InlineData(1, 1, PlayerItemBridgeStage.ReconcileRequired, 2)]
    public void ActualReturnedAndPartialQuantitiesStayAccountedFor(long consumed, long returned,
        PlayerItemBridgeStage expectedStage, long expectedReserved)
    {
        var storage = new MemoryStorage();
        var archive = Load(storage);
        var item = new Item { Code = new AssetLocation("game:stone"), MaxStackSize = 64 };
        var encoded = ItemStackCodec.Encode(new ItemStack(item, 2));
        var store = archive.Directory.ForPlayer("player");
        var inserted = store.Insert("seed", new PlayerItemPrototype(PlayerItemStorageKind.Aggregate,
            "game:stone", "materials", "Stone", encoded.Payload.ExactState), 2, 0);
        long destination = 0;
        var bridge = new PlayerItemBridgeCoordinator(archive, new Resolver(item));
        bridge.Register(new EngineItemBridgeAdapter("use", PlayerItemBridgeOperation.Use,
            (_, _) => { destination = consumed; return new(true, consumed, returned, "actual quantities"); },
            _ => new(true, consumed, returned, "actual quantities")));
        var receipt = bridge.Execute(new("use", "player", PlayerItemBridgeOperation.Use,
            "use", inserted.EntryId, 2, "canonical", "effect", store.Revision));
        Assert.Equal(expectedStage, receipt.Stage);
        Assert.Equal(consumed, receipt.ExternalResult!.Consumed);
        Assert.Equal(returned, receipt.ExternalResult.Returned);
        var restored = Assert.Single(Load(storage).Directory.ForPlayer("player").Query(new("", "", 0, 100)).Entries);
        Assert.Equal(2, restored.Quantity);
        Assert.Equal(expectedReserved, restored.ReservedQuantity);
        Assert.Equal(consumed, destination);
    }
    [Fact]
    public void LegacyPolicyFlagCannotAuthorizeMaterializingActualEvolvingState()
    {
        var storage = new MemoryStorage();
        var archive = Load(storage);
        var item = new Item { Code = new AssetLocation("game:stone"), MaxStackSize = 64 };
        var stack = new ItemStack(item, 1);
        stack.Attributes["temperature"] = new Vintagestory.API.Datastructures.TreeAttribute();
        var encoded = ItemStackCodec.Encode(stack);
        var store = archive.Directory.ForPlayer("player");
        var inserted = store.Insert("legacy", new PlayerItemPrototype(PlayerItemStorageKind.Aggregate,
            "game:stone", "materials", "Stone", encoded.Payload.ExactState), 1, 0);
        int destination = 0;
        var bridge = new PlayerItemBridgeCoordinator(archive, new Resolver(item));
        bridge.Register(new EngineItemBridgeAdapter("test", PlayerItemBridgeOperation.Drop,
            (_, _) => { destination++; return new(true, 1, 0, "effect"); }, _ => new(false, 0, 0, "unknown")));
        Assert.Equal(PlayerItemBridgeStage.Refused, bridge.Execute(new("drop", "player", PlayerItemBridgeOperation.Drop,
            "test", inserted.EntryId, 1, "canonical", "world", store.Revision)).Stage);
        Assert.Equal(0, destination);
        Assert.Equal(1, Assert.Single(store.Query(new("", "", 0, 100)).Entries).Quantity);
    }
    [Fact]
    public void AggregatedRewardClaimsRestartWithoutDuplicatingTheirIndividualQuantities()
    {
        var storage = new MemoryStorage();
        var archive = Load(storage);
        var item = new Item { Code = new AssetLocation("game:stone"), MaxStackSize = 64 };
        var bridge = new PlayerItemBridgeCoordinator(archive, new Resolver(item));
        bridge.RegisterClaimProducer("reward", PlayerItemBridgeOperation.Reward);
        var request = new PlayerItemBridgeRequest("first", "player", PlayerItemBridgeOperation.Reward,
            "reward", "", 2, "objective", "canonical", 0);
        Assert.Equal(PlayerItemBridgeStage.Completed, bridge.RegisterClaim(request, new ItemStack(item, 2)).Stage);
        Assert.Equal(PlayerItemBridgeStage.Completed, bridge.RegisterClaim(request with { OperationId = "second" }, new ItemStack(item, 2)).Stage);
        bridge = new PlayerItemBridgeCoordinator(Load(storage), new Resolver(item));
        bridge.RegisterClaimProducer("reward", PlayerItemBridgeOperation.Reward);
        Assert.Equal(PlayerItemBridgeStage.Completed, bridge.RegisterClaim(request, new ItemStack(item, 2)).Stage);
        Assert.Equal(4, Assert.Single(bridge.Archive.Directory.ForPlayer("player").Query(new("", "", 0, 100)).Entries).Quantity);
    }
    [Fact]
    public void APartialExternalReceiptCannotMasqueradeAsACommittableStage()
    {
        var receipt = new PlayerItemBridgeReceipt
        {
            Request = new("partial", "player", PlayerItemBridgeOperation.Use, "use", "entry", 2, "canonical", "effect", 0),
            Stage = PlayerItemBridgeStage.ExternalRecorded, ReservationId = "reservation",
            Amounts = new[] { new PlayerItemAmount("entry", 2) },
            ExternalResult = new(true, 1, 1, "partial")
        };
        var serializer = new PlayerItemArchiveSerializer();
        var archive = new PlayerItemArchive { Bridges = new() { ["partial"] = receipt } };
        Assert.False(serializer.TryDeserialize(serializer.Serialize(archive), out _, out _));
    }

    [Fact]
    public void AnOrphanReceiptCannotActivateAStoreWithoutItsReservation()
    {
        var storage = new MemoryStorage();
        var repository = new PlayerItemArchiveRepository(storage);
        var receipt = new PlayerItemBridgeReceipt
        {
            Request = new("orphan", "player", PlayerItemBridgeOperation.Drop, "drop", "entry", 1, "canonical", "world", 0),
            Stage = PlayerItemBridgeStage.Prepared, ReservationId = "missing",
            Amounts = new[] { new PlayerItemAmount("entry", 1) }
        };
        Assert.True(repository.Save(new PlayerItemArchive
        {
            Players = new() { ["player"] = new() }, Bridges = new() { ["orphan"] = receipt }
        }).Success);
        var archive = new PlayerItemArchiveService(repository);
        Assert.False(archive.Load().Success);
        Assert.False(archive.IsLoaded);
    }
    [Fact]
    public void LoadedBirthIdentityCannotAuthorizeAChangedSourceSnapshot()
    {
        var item = new Item { Code = new AssetLocation("game:stone"), MaxStackSize = 64 };
        var entity = new EntityItem { Itemstack = new ItemStack(item, 3) };
        entity.WatchedAttributes.SetString(EngineItemPickupSource.BirthTokenAttribute, Guid.NewGuid().ToString("N"));
        EngineItemPickupSource.Observe(entity, loaded: true);
        var source = new EngineItemPickupSource(entity);
        Assert.True(source.HasPersistedIdentity);
        entity.Itemstack.StackSize = 4;
        Assert.False(source.HasPersistedIdentity);
    }
    [Fact]
    public void MalformedAdapterReceiptCannotPoisonTheDurableArchive()
    {
        var storage = new MemoryStorage();
        var archive = Load(storage);
        var item = new Item { Code = new AssetLocation("game:stone"), MaxStackSize = 64 };
        var encoded = ItemStackCodec.Encode(new ItemStack(item, 1));
        var store = archive.Directory.ForPlayer("player");
        var inserted = store.Insert("seed", new PlayerItemPrototype(PlayerItemStorageKind.Aggregate,
            "game:stone", "materials", "Stone", encoded.Payload.ExactState), 1, 0);
        var bridge = new PlayerItemBridgeCoordinator(archive, new Resolver(item));
        bridge.Register(new EngineItemBridgeAdapter("malformed", PlayerItemBridgeOperation.Drop,
            (_, _) => new(true, -1, 2, "invalid"), _ => new(false, 0, 0, "unknown")));
        var result = bridge.Execute(new("invalid-result", "player", PlayerItemBridgeOperation.Drop,
            "malformed", inserted.EntryId, 1, "canonical", "world", store.Revision));
        Assert.Equal(PlayerItemBridgeStage.ReconcileRequired, result.Stage);
        Assert.True(archive.SaveDirty().Success);
        Assert.Equal(1, Assert.Single(Load(storage).Directory.ForPlayer("player").Query(new("", "", 0, 100)).Entries).ReservedQuantity);
    }

    [Fact]
    public void BridgeReceiptExtensionsSurviveSaveAndUnknownVersionBlocksActivation()
    {
        var storage = new MemoryStorage();
        var archive = Load(storage);
        var item = new Item { Code = new AssetLocation("game:stone"), MaxStackSize = 64 };
        var bridge = new PlayerItemBridgeCoordinator(archive, new Resolver(item));
        bridge.RegisterClaimProducer("reward", PlayerItemBridgeOperation.Reward);
        bridge.RegisterClaim(new("claim", "player", PlayerItemBridgeOperation.Reward, "reward", "", 1,
            "objective", "canonical", 0), new ItemStack(item, 1));
        var receipt = Assert.Single(new PlayerItemBridgeJournal(archive).Snapshot());
        receipt.ExtensionData = new Dictionary<string, Newtonsoft.Json.Linq.JToken> { ["future"] = new Newtonsoft.Json.Linq.JObject { ["value"] = 42 } };
        Assert.True(new PlayerItemBridgeJournal(archive).Save(receipt));
        Assert.Equal(42, (int)new PlayerItemBridgeJournal(Load(storage)).Snapshot()[0].ExtensionData!["future"]["value"]!);
        receipt.Version = 999;
        Assert.False(new PlayerItemBridgeJournal(archive).Save(receipt));
    }
    [Fact]
    public void NativeActivationRequiresEveryExposedProducerGuardAndReceiptAdapter()
    {
        var coverage = new PlayerItemBridgeCoverage();
        Assert.False(coverage.CanActivate(new[] { "pickup", "sign" }));
        coverage.Register("pickup", () => true);
        Assert.False(coverage.CanActivate(new[] { "pickup", "sign" }));
        bool guardReady = true;
        coverage.Register("sign", () => guardReady);
        Assert.True(coverage.CanActivate(new[] { "pickup", "sign" }));
        guardReady = false;
        Assert.False(coverage.CanActivate(new[] { "pickup", "sign" }));
        Assert.False(coverage.CanActivate(Array.Empty<string>()));
    }
    [Fact]
    public void PickupTombstoneRemovesRestoredSourceAndDoesNotGrantAgain()
    {
        var storage = new MemoryStorage();
        var archive = Load(storage);
        var item = new Item { Code = new AssetLocation("game:stone"), MaxStackSize = 64 };
        var source = new PickupSource(new ItemStack(item, 7));
        var bridge = new PlayerItemBridgeCoordinator(archive, new Resolver(item));
        Assert.Equal(PlayerItemBridgeStage.Completed, bridge.Pickup("player", source).Stage);
        Assert.Equal(0, source.Stack!.StackSize);
        bridge = new PlayerItemBridgeCoordinator(Load(storage), new Resolver(item));
        var changedSource = new PickupSource(new ItemStack(item, 8));
        Assert.False(bridge.SuppressClaimedPickup(changedSource));
        Assert.Equal(8, changedSource.Stack!.StackSize);
        var restoredSource = new PickupSource(new ItemStack(item, 7));
        Assert.True(bridge.SuppressClaimedPickup(restoredSource));
        Assert.Equal(0, restoredSource.Stack!.StackSize);
        Assert.Equal(7, Assert.Single(bridge.Archive.Directory.ForPlayer("player").Query(new("", "", 0, 100)).Entries).Quantity);
        var unconfirmed = new PickupSource(new ItemStack(item, 5)) { HasPersistedIdentity = false };
        Assert.Equal(PlayerItemBridgeStage.Refused, bridge.Pickup("player", unconfirmed).Stage);
        Assert.Equal(5, unconfirmed.Stack!.StackSize);
    }

    private sealed class PickupSource(ItemStack stack) : IEngineItemPickupSource
    {
        public string Token => "persisted-birth-token";
        public bool HasPersistedIdentity { get; set; } = true;
        public ItemStack? Stack => stack;
        public void Remove() => stack.StackSize = 0;
    }
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 1)]
    public void InterruptedTransitionPreservesBothCustodyTotals(int failedWrite, int expectedDestination)
    {
        var storage = new MemoryStorage();
        var archive = Load(storage);
        var item = new Item { Code = new AssetLocation("game:stone"), MaxStackSize = 64 };
        var encoded = ItemStackCodec.Encode(new ItemStack(item, 1));
        var store = archive.Directory.ForPlayer("player");
        var inserted = store.Insert("seed", new PlayerItemPrototype(PlayerItemStorageKind.Aggregate,
            "game:stone", "materials", "Stone", encoded.Payload.ExactState), 1, 0);
        Assert.True(archive.SaveDirty().Success);
        storage.FailAtWrite = storage.Writes + failedWrite;
        int destination = 0;
        var adapter = new EngineItemBridgeAdapter("test", PlayerItemBridgeOperation.Drop,
            (_, stack) => { destination += stack.StackSize; return new(true, 1, 0, "effect"); },
            _ => new(true, destination, 1 - destination, "observed destination"));
        var bridge = new PlayerItemBridgeCoordinator(archive, new Resolver(item));
        bridge.Register(adapter);
        var request = new PlayerItemBridgeRequest("operation", "player", PlayerItemBridgeOperation.Drop,
            "test", inserted.EntryId, 1, "canonical", "world", store.Revision);
        bridge.Execute(request);
        bridge = new PlayerItemBridgeCoordinator(Load(storage), new Resolver(item));
        bridge.Register(adapter);
        bridge.Execute(request);
        Assert.Equal(expectedDestination, destination);
        Assert.Equal(1 - expectedDestination, bridge.Archive.Directory.ForPlayer("player")
            .Query(new("", "", 0, 100)).Entries.Sum(entry => entry.Quantity));
    }

    [Fact]
    public void RegisteredRewardRetainsDurableClaimWhenCapacityIsFull()
    {
        var storage = new MemoryStorage();
        var archive = Load(storage);
        archive.Directory.Set("player", new InMemoryPlayerItemStore(capacity: new PlayerItemCapacity(1, 1, 1)));
        var item = new Item { Code = new AssetLocation("game:stone"), MaxStackSize = 64 };
        var bridge = new PlayerItemBridgeCoordinator(archive, new Resolver(item));
        bridge.RegisterClaimProducer("vrpg:reward", PlayerItemBridgeOperation.Reward);
        var request = new PlayerItemBridgeRequest("reward", "player", PlayerItemBridgeOperation.Reward,
            "vrpg:reward", "", 2, "objective:1", "canonical", 0);
        Assert.Equal(PlayerItemBridgeStage.Prepared, bridge.RegisterClaim(request, new ItemStack(item, 2)).Stage);
        var restarted = Load(storage);
        var receipt = Assert.Single(new PlayerItemBridgeJournal(restarted).Snapshot());
        Assert.Equal(2, receipt.Request.Quantity);
        Assert.NotNull(receipt.Claim);
        Assert.Empty(restarted.Directory.ForPlayer("player").Query(new("", "", 0, 100)).Entries);
        Assert.Equal(PlayerItemBridgeStage.Refused,
            bridge.RegisterClaim(request with { OperationId = "bad", AdapterCode = "unregistered" }, new ItemStack(item, 2)).Stage);
    }

    [Fact]
    public void WorldCheckpointInterruptionRefusesOlderWorldRatherThanLoadingWritableCustody()
    {
        string root = Path.Combine(Path.GetTempPath(), "vrpg-checkpoint-" + Guid.NewGuid().ToString("N"));
        try
        {
            var legacy = new MemoryStorage();
            var durable = new WorldScopedPlayerItemArchiveStorage(root, "world", legacy);
            Assert.Null(durable.Read(PlayerItemArchiveRepository.SlotAKey));
            legacy.FailAtWrite = 1;
            Assert.Throws<IOException>(() => durable.AdvanceWorldCheckpoint());
            var archive = new PlayerItemArchiveService(new PlayerItemArchiveRepository(
                new WorldScopedPlayerItemArchiveStorage(root, "world", legacy)));
            Assert.False(archive.Load().Success);
            Assert.False(archive.IsLoaded);
            Assert.False(archive.SaveDirty().Success);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public void OutboundRetryAndRaceCannotConsumeTheFinalUnitTwice()
    {
        var storage = new MemoryStorage();
        var archive = Load(storage);
        var item = new Item { Code = new AssetLocation("game:stone"), MaxStackSize = 64 };
        var stack = new ItemStack(item, 1);
        var encoded = ItemStackCodec.Encode(stack);
        var store = archive.Directory.ForPlayer("player");
        var inserted = store.Insert("seed", new PlayerItemPrototype(PlayerItemStorageKind.Aggregate,
            "game:stone", "materials", "Stone", encoded.Payload.ExactState), 1, 0);
        int destination = 0;
        var bridge = new PlayerItemBridgeCoordinator(archive, new Resolver(item));
        bridge.Register(new EngineItemBridgeAdapter("test-drop", PlayerItemBridgeOperation.Drop,
            (_, materialized) => { destination += materialized.StackSize; return new(true, materialized.StackSize, 0, "world-drop"); },
            _ => new(false, 0, 0, "unknown")));
        var request = new PlayerItemBridgeRequest("drop-1", "player", PlayerItemBridgeOperation.Drop,
            "test-drop", inserted.EntryId, 1, "canonical", "world:position", store.Revision);
        var results = new PlayerItemBridgeReceipt[2];
        Parallel.Invoke(() => results[0] = bridge.Execute(request),
            () => results[1] = bridge.Execute(request with { OperationId = "drop-2" }));
        Assert.Single(results.Where(r => r.Stage == PlayerItemBridgeStage.Completed));
        Assert.Equal(1, destination);
        Assert.Empty(store.Query(new("", "", 0, 100)).Entries);
        var completed = results.Single(r => r.Stage == PlayerItemBridgeStage.Completed);
        Assert.Equal(PlayerItemBridgeStage.Completed, bridge.Execute(completed.Request).Stage);
        Assert.Equal(1, destination);
        var restarted = new PlayerItemBridgeCoordinator(Load(storage), new Resolver(item));
        Assert.Equal(PlayerItemBridgeStage.Completed, restarted.Execute(completed.Request).Stage);
        Assert.Empty(restarted.Archive.Directory.ForPlayer("player").Query(new("", "", 0, 100)).Entries);
    }

    [Fact]
    public void UnknownExternalEffectRetainsReservationAcrossRestartWithoutReplay()
    {
        var storage = new MemoryStorage();
        var archive = Load(storage);
        var item = new Item { Code = new AssetLocation("game:stone"), MaxStackSize = 64 };
        var encoded = ItemStackCodec.Encode(new ItemStack(item, 1));
        var store = archive.Directory.ForPlayer("player");
        var inserted = store.Insert("seed", new PlayerItemPrototype(PlayerItemStorageKind.Aggregate,
            "game:stone", "materials", "Stone", encoded.Payload.ExactState), 1, 0);
        int destination = 0;
        var bridge = new PlayerItemBridgeCoordinator(archive, new Resolver(item));
        var adapter = new EngineItemBridgeAdapter("test-drop", PlayerItemBridgeOperation.Drop,
            (_, stack) => { destination += stack.StackSize; throw new IOException("lost response"); },
            _ => new(false, 0, 0, "unknown"));
        bridge.Register(adapter);
        var request = new PlayerItemBridgeRequest("drop", "player", PlayerItemBridgeOperation.Drop,
            "test-drop", inserted.EntryId, 1, "canonical", "world:position", store.Revision);
        Assert.Equal(PlayerItemBridgeStage.ReconcileRequired, bridge.Execute(request).Stage);
        bridge = new PlayerItemBridgeCoordinator(Load(storage), new Resolver(item));
        bridge.Register(adapter);
        Assert.Equal(PlayerItemBridgeStage.ReconcileRequired, bridge.Execute(request).Stage);
        Assert.Equal(1, destination);
        var entry = Assert.Single(bridge.Archive.Directory.ForPlayer("player").Query(new("", "", 0, 100)).Entries);
        Assert.Equal(1, entry.Quantity);
        Assert.Equal(1, entry.ReservedQuantity);
    }

    private static PlayerItemArchiveService Load(MemoryStorage storage)
    {
        var archive = new PlayerItemArchiveService(new PlayerItemArchiveRepository(storage));
        Assert.True(archive.Load().Success);
        return archive;
    }

    private sealed class Resolver(Item item) : ICollectibleResolver
    {
        public Item? GetItem(AssetLocation code) => code.Equals(item.Code) ? item : null;
        public Block? GetBlock(AssetLocation code) => null;
    }
    [Fact]
    public void DurableArchiveReopensCompleteLegacySnapshotAndNeverFallsBackOnCorruption()
    {
        string root = Path.Combine(Path.GetTempPath(), "vrpg-bridge-" + Guid.NewGuid().ToString("N"));
        try
        {
            var legacy = new MemoryStorage();
            var repository = new PlayerItemArchiveRepository(legacy);
            Assert.True(repository.Save(new PlayerItemArchive()).Success);
            var durable = new WorldScopedPlayerItemArchiveStorage(root, "world-a", legacy);
            var loaded = new PlayerItemArchiveRepository(durable).Load();
            Assert.True(loaded.Success, loaded.Error);
            Assert.Equal(1, loaded.Generation);
            Assert.True(new PlayerItemArchiveRepository(durable).Save(new PlayerItemArchive()).Success);
            byte[] expectedSlot = durable.Read(PlayerItemArchiveRepository.SlotBKey)!;
            var reopened = new WorldScopedPlayerItemArchiveStorage(root, "world-a", legacy);
            Assert.Equal(expectedSlot, reopened.Read(PlayerItemArchiveRepository.SlotBKey));
            Assert.Null(new WorldScopedPlayerItemArchiveStorage(root, "world-b", new MemoryStorage())
                .Read(PlayerItemArchiveRepository.SlotAKey));
            File.WriteAllText(durable.ArchivePath, "broken");
            Assert.ThrowsAny<Exception>(() =>
                new WorldScopedPlayerItemArchiveStorage(root, "world-a", legacy).Read(PlayerItemArchiveRepository.SlotAKey));
            File.Delete(durable.ArchivePath);
            Assert.ThrowsAny<Exception>(() =>
                new WorldScopedPlayerItemArchiveStorage(root, "world-a", legacy).Read(PlayerItemArchiveRepository.SlotAKey));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class MemoryStorage : IDurablePlayerItemArchiveStorage
    {
        public bool IsCrashDurable => true;
        private readonly Dictionary<string, byte[]> values = new();
        public int Writes { get; private set; }
        public int FailAtWrite { get; set; }
        public byte[]? Read(string key) => values.TryGetValue(key, out var value) ? (byte[])value.Clone() : null;
        public void Write(string key, byte[] data)
        {
            if (++Writes == FailAtWrite) throw new IOException("Interrupted storage write.");
            values[key] = (byte[])data.Clone();
        }
    }
}
