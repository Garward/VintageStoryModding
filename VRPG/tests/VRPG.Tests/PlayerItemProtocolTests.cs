using System;
using System.Collections.Generic;
using ProtoBuf;
using VRPG.Network;
using VRPG.Config;
using VRPG.Inventory.Gear;
using VRPG.Inventory.Networking;
using VRPG.Inventory.Queries;
using VRPG.Modules.Rpg.Players;
using VRPG.Modules.Rpg.Talents;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Common;
using VRPG.Inventory;
using Xunit;

namespace VRPG.Tests;

public sealed class PlayerItemProtocolTests
{
    [Theory]
    [InlineData("rate-limit")]
    [InlineData("protocol-version")]
    [InlineData("unauthorized-player")]
    public void DetailAdmissionFailureRoundTripsBoundedSelectionIdentity(string reason)
    {
        var f = new Fixture();
        if (reason == "rate-limit") for (int i = 0; i < 20; i++) f.Service.Query(f.Player, f.Query());
        var request = new PlayerItemDetailRequest { ConnectionGeneration = 1, QueryId = 9,
            EntryId = "selected-entry", RoleCode = PlayerEquipmentRoles.Head, ProtocolVersion = reason == "protocol-version" ? 99 : 1 };
        var result = Serializer.DeepClone(f.Service.Detail(reason == "unauthorized-player" ? new TestPlayer("other") : f.Player, request));
        Assert.Equal(reason, result.RejectionCode); Assert.Equal("selected-entry", result.EntryId);
        Assert.Equal(PlayerEquipmentRoles.Head, result.RoleCode); Assert.Equal(9, result.QueryId);
        Assert.Equal(0, result.StoreRevision);
        request.EntryId = new('x', 129); request.RoleCode = "bad\nrole";
        result = Serializer.DeepClone(f.Service.Detail(f.Player, request));
        Assert.Equal("", result.EntryId); Assert.Equal("", result.RoleCode);
    }

    [Fact]
    public void CategoryPageProtocolPreservesOffsetCountAndRejectsOversizedRequests()
    {
        var request = Serializer.DeepClone(new PlayerItemQueryRequest { ConnectionGeneration = 1, QueryId = 1, CategoryOffset = 128, CategoryLimit = 128 });
        Assert.Equal(128, request.CategoryOffset);
        var result = Serializer.DeepClone(new PlayerItemQueryResult { CategoryTotal = 200,
            Categories = new[] { new PlayerItemCategoryPacket { Code = "addon", DisplayName = "Addon", Count = 7 } } });
        Assert.Equal(200, result.CategoryTotal); Assert.Equal(7, Assert.Single(result.Categories).Count);
        request.CategoryLimit = 129;
        Assert.Equal("request-limit", PlayerItemQueryService.Validate(request));
    }
    [Theory]
    [InlineData("VRPG.Network.PlayerItemQueryRequest")]
    [InlineData("VRPG.Network.PlayerItemActionRequest")]
    [InlineData("VRPG.Network.PlayerItemDetailResult")]
    public void BoundedMenuContractsExist(string name)
    {
        Assert.NotNull(typeof(IPlayerItemStore).Assembly.GetType(name));
    }

    [Theory]
    [InlineData(9_007_199_254_740_993L)]
    [InlineData(long.MaxValue)]
    public void QuantitiesRoundTripWithoutFloatingPoint(long quantity)
    {
        var request = Serializer.DeepClone(new PlayerItemActionRequest { Quantity = quantity });
        var result = Serializer.DeepClone(new PlayerItemQueryResult
        {
            Entries = new[] { new PlayerItemEntryPacket { Quantity = quantity, ReservedQuantity = quantity } },
            CapacitySummary = new() { AggregateQuantityPerEntry = quantity }
        });
        Assert.Equal(quantity, request.Quantity);
        Assert.Equal(quantity, result.Entries[0].Quantity);
        Assert.Equal(quantity, result.Entries[0].ReservedQuantity);
        Assert.Equal(quantity, result.CapacitySummary.AggregateQuantityPerEntry);
    }

    [Fact]
    public void PlayerIdentityComesOnlyFromConnectionAndReconnectInvalidatesOldSender()
    {
        var f = new Fixture();
        var stranger = new TestPlayer("other");
        Assert.Equal("unauthorized-player", f.Service.Query(stranger, f.Query()).RejectionCode);
        f.Service.Connect(stranger);
        var request = f.Query(); request.ConnectionGeneration = 2;
        Assert.Empty(f.Service.Query(stranger, request).Entries);
        var replacement = new TestPlayer("p");
        f.Service.Connect(replacement);
        Assert.Equal("unauthorized-player", f.Service.Query(f.Player, f.Query()).RejectionCode);
        Assert.Null(typeof(PlayerItemQueryRequest).GetProperty("PlayerUID"));
        Assert.Null(typeof(PlayerItemActionRequest).GetProperty("PlayerUID"));
    }

    [Fact]
    public void ActionReplayAndConflictingRequestsAreSafeAndNeverMutateStore()
    {
        var f = new Fixture();
        Assert.Equal("", f.Service.Query(f.Player, f.Query()).RejectionCode);
        var request = f.Action();
        var first = f.Service.Action(f.Player, request);
        Assert.Equal("unsupported-action", first.RejectionCode);
        Assert.Equal(first.RejectionCode, f.Service.Action(f.Player, request).RejectionCode);
        request.Quantity++;
        Assert.Equal("request-id-conflict", f.Service.Action(f.Player, request).RejectionCode);
        Assert.Equal(1, f.Items.ForPlayer("p").Revision);
    }

    [Fact]
    public void ActionRequiresRefreshThenExactStoreAndBuildRevisions()
    {
        var f = new Fixture();
        Assert.Equal("refresh-required", f.Service.Action(f.Player, f.Action()).RejectionCode);
        f.Service.Query(f.Player, f.Query());
        var request = f.Action(2); request.ExpectedStoreRevision = 0;
        Assert.Equal("stale-store", f.Service.Action(f.Player, request).RejectionCode);
        f.Players.GetOrCreate("p").BaseStats["strength"] = 10;
        request = f.Action(3); request.ExpectedBuildRevision = 1;
        Assert.Equal("stale-build", f.Service.Action(f.Player, request).RejectionCode);
        request = f.Action(4); request.ConnectionGeneration = 99;
        Assert.Equal("connection-generation", f.Service.Action(f.Player, request).RejectionCode);
    }

    [Fact]
    public void EquipAndUnequipUseCanonicalGearAndReturnAffectedEntries()
    {
        var f = new Fixture();
        string gearId = f.InsertGear(requiredLevel: 1);
        f.Refresh();

        PlayerItemActionResult equipped = f.Service.Action(f.Player, f.Action(
            PlayerItemAction.Equip, gearId, PlayerEquipmentRoles.MainWeapon, 1));

        Assert.True(equipped.Success);
        Assert.Equal("", equipped.RejectionCode);
        Assert.Equal(PlayerItemLocation.Equipped, Assert.Single(equipped.ChangedEntries).Location);
        Assert.Equal(gearId, equipped.ChangedEntries[0].EntryId);
        Assert.Equal(new[] { "inventory", "equipment", "character" }, equipped.InvalidatedScopes);
        Assert.Equal(gearId, Assert.Single(f.Store.Equipment().Entries).EntryId);

        f.Refresh();
        PlayerItemActionRequest unequipRequest = f.Action(
            PlayerItemAction.Unequip, gearId, PlayerEquipmentRoles.MainWeapon, 2);
        PlayerItemActionResult unequipped = f.Service.Action(f.Player, unequipRequest);

        Assert.True(unequipped.Success);
        Assert.Equal(PlayerItemLocation.Stored, Assert.Single(unequipped.ChangedEntries).Location);
        Assert.Equal(gearId, unequipped.ChangedEntries[0].EntryId);
        Assert.Equal(new[] { "inventory", "equipment", "character" }, unequipped.InvalidatedScopes);
        Assert.Empty(f.Store.Equipment().Entries);
        PlayerItemActionResult replay = f.Service.Action(f.Player, unequipRequest);
        Assert.True(replay.Success);
        Assert.Equal(unequipped.StoreRevision, replay.StoreRevision);
        Assert.Equal(Assert.Single(unequipped.ChangedEntries).EntryId, Assert.Single(replay.ChangedEntries).EntryId);
    }

    [Fact]
    public void ReplacingEquippedGearReturnsBothAuthoritativelyChangedEntries()
    {
        var f = new Fixture();
        f.Players.GetOrCreate("p").Level = 20;
        string firstGearId = f.InsertGear(requiredLevel: 20);
        string secondGearId = f.InsertGear(requiredLevel: 20);
        f.Refresh();
        Assert.True(f.Service.Action(f.Player, f.Action(
            PlayerItemAction.Equip, firstGearId, PlayerEquipmentRoles.MainWeapon, 1)).Success);
        f.Refresh();

        PlayerItemActionResult result = f.Service.Action(f.Player, f.Action(
            PlayerItemAction.Equip, secondGearId, PlayerEquipmentRoles.MainWeapon, 2));

        Assert.True(result.Success);
        Assert.Equal(2, result.ChangedEntries.Length);
        Assert.Contains(result.ChangedEntries, entry => entry.EntryId == firstGearId && entry.Location == PlayerItemLocation.Stored);
        Assert.Contains(result.ChangedEntries, entry => entry.EntryId == secondGearId && entry.Location == PlayerItemLocation.Equipped);
    }

    [Fact]
    public void ChangedEntriesUseTheCanonicalCategoryProjection()
    {
        var f = new Fixture(categories: new PlayerItemCategoryRegistry(new[]
        {
            new PlayerItemCategoryDefinition("addon", "Addon Gear")
        }));
        f.Players.GetOrCreate("p").Level = 20;
        f.Refresh();
        string displacedId = f.InsertGear(requiredLevel: 1, category: "addon");
        string gearId = f.InsertGear(requiredLevel: 1);
        Assert.True(f.Store.Equip("fixture-equip", displacedId, PlayerEquipmentRoles.MainWeapon, f.Store.Revision).Success);

        PlayerItemActionResult result = f.Service.Action(f.Player, f.Action(
            PlayerItemAction.Equip, gearId, PlayerEquipmentRoles.MainWeapon, 1));

        PlayerItemEntryPacket displaced = Assert.Single(result.ChangedEntries, entry => entry.EntryId == displacedId);
        Assert.Equal("addon", displaced.Category);
        Assert.Equal("addon", displaced.AuthoredCategory);
        Assert.Equal("Addon Gear", displaced.CategoryName);
    }

    [Fact]
    public void CommittedEquipDoesNotReturnOverlongOrControlCharacterChangedMetadata()
    {
        var ids = new Queue<string>(new[] { "item", new string('a', 129), "target" });
        var f = new Fixture(ids.Dequeue);
        f.Players.GetOrCreate("p").Level = 20;
        f.Refresh();
        string displacedId = f.InsertGear(requiredLevel: 1, displayName: "bad\nname");
        string gearId = f.InsertGear(requiredLevel: 1);
        Assert.True(f.Store.Equip("fixture-equip", displacedId, PlayerEquipmentRoles.MainWeapon, f.Store.Revision).Success);

        PlayerItemActionResult result = f.Service.Action(f.Player, f.Action(
            PlayerItemAction.Equip, gearId, PlayerEquipmentRoles.MainWeapon, 1));

        Assert.True(result.Success);
        Assert.Empty(result.ChangedEntries);
        Assert.Equal(new[] { "inventory", "equipment", "character" }, result.InvalidatedScopes);
        Assert.Equal(gearId, Assert.Single(f.Store.Equipment().Entries).EntryId);
    }

    [Fact]
    public void EquipRejectsStaleInvalidMissingAndUnmetRequestsWithoutMutation()
    {
        var f = new Fixture();
        string gearId = f.InsertGear(requiredLevel: 20);
        f.Refresh();
        long revision = f.Store.Revision;

        Assert.Equal("stale-store", f.Service.Action(f.Player, f.Action(
            PlayerItemAction.Equip, gearId, PlayerEquipmentRoles.MainWeapon, 1, revision - 1)).RejectionCode);
        Assert.Equal("equipment-role-mismatch", f.Service.Action(f.Player, f.Action(
            PlayerItemAction.Equip, gearId, "armor.invalid", 2)).RejectionCode);
        Assert.Equal("entry-missing", f.Service.Action(f.Player, f.Action(
            PlayerItemAction.Equip, "missing", PlayerEquipmentRoles.MainWeapon, 3)).RejectionCode);
        Assert.Equal("requirement-not-met", f.Service.Action(f.Player, f.Action(
            PlayerItemAction.Equip, gearId, PlayerEquipmentRoles.MainWeapon, 4)).RejectionCode);
        Assert.Equal(revision, f.Store.Revision);
        Assert.Empty(f.Store.Equipment().Entries);
    }

    [Fact]
    public void SuccessfulEquipReplayReturnsTheOriginalResultWithoutRepeatingMutation()
    {
        var f = new Fixture();
        f.Players.GetOrCreate("p").Level = 20;
        string gearId = f.InsertGear(requiredLevel: 20);
        f.Refresh();
        PlayerItemActionRequest request = f.Action(
            PlayerItemAction.Equip, gearId, PlayerEquipmentRoles.MainWeapon, 1);

        PlayerItemActionResult first = f.Service.Action(f.Player, request);
        PlayerItemActionResult replay = f.Service.Action(f.Player, request);

        Assert.True(first.Success);
        Assert.True(replay.Success);
        Assert.Equal(first.StoreRevision, replay.StoreRevision);
        Assert.Equal(first.BuildRevision, replay.BuildRevision);
        Assert.Equal(Assert.Single(first.ChangedEntries).EntryId, Assert.Single(replay.ChangedEntries).EntryId);
        Assert.Equal(first.StoreRevision, f.Store.Revision);
        Assert.Equal(gearId, Assert.Single(f.Store.Equipment().Entries).EntryId);
    }

    [Fact]
    public void RateLimitRunsBeforeStoreLookupAndRecoversNextWindow()
    {
        var f = new Fixture();
        for (int i = 0; i < PlayerItemNetworkService.RequestsPerSecond; i++)
            Assert.Equal("", f.Service.Query(f.Player, f.Query()).RejectionCode);
        Assert.Equal("rate-limit", f.Service.Query(f.Player, f.Query()).RejectionCode);
        f.Now += 1;
        Assert.Equal("", f.Service.Query(f.Player, f.Query()).RejectionCode);
    }

    [Fact]
    public void ReplayEvictionCannotReexecuteOldSequence()
    {
        var f = new Fixture();
        f.Service.Query(f.Player, f.Query());
        var old = f.Action();
        for (int i = 1; i <= 130; i++)
        {
            f.Now += 1;
            Assert.Equal("unsupported-action", f.Service.Action(f.Player, f.Action(i)).RejectionCode);
        }
        Assert.Equal("request-expired", f.Service.Action(f.Player, old).RejectionCode);
    }

    [Fact]
    public void InvalidActionsAndIdentitiesFailBeforeAuthorityWork()
    {
        var f = new Fixture();
        var request = f.Action(); request.Action = (PlayerItemAction)99;
        Assert.Equal("invalid-action", f.Service.Action(f.Player, request).RejectionCode);
        request = f.Action(); request.EntryId = "\n";
        Assert.Equal("invalid-identity", f.Service.Action(f.Player, request).RejectionCode);
        request = f.Action(); request.PreviewToken = new('x', 257);
        Assert.Equal("invalid-identity", f.Service.Action(f.Player, request).RejectionCode);
        request = f.Action(); request.Quantity = long.MinValue;
        Assert.Equal("invalid-quantity", f.Service.Action(f.Player, request).RejectionCode);
    }

    [Fact]
    public void ServerPageAndClientInspectTenThousandUnitsAsOneEntry()
    {
        var f = new Fixture();
        var client = new VRPG.Client.PlayerMenu.Inventory.PlayerItemClientService();
        client.BeginSession(new() { ConnectionGeneration = 1 });
        client.QueueQuery(new(), 0);
        var request = client.Pump(200)!;
        var response = Serializer.DeepClone(f.Service.Query(f.Player, request));
        Assert.True(client.Receive(response));
        Assert.Equal(10000, Assert.Single(client.CurrentPage!.Entries).Quantity);
        Assert.True(client.CanWrite);
    }

    [Fact]
    public void QuerySelectionDiscoversGearRoleWithoutKnowingItsVisualChassis()
    {
        var f = new Fixture();
        f.Players.GetOrCreate("p").Level = 20;
        var gear = GearReadinessValidatorTests.Instance();
        var store = f.Items.ForPlayer("p");
        store.Insert("gear", new(PlayerItemStorageKind.Unique, "game:gear-rusty", PlayerItemCategories.Gear,
            "Shared fallback appearance", GearItemStateCodec.Encode(gear)), 1, store.Revision);
        var client = new VRPG.Client.PlayerMenu.Inventory.PlayerItemClientService();
        client.BeginSession(new() { ConnectionGeneration = 1 });
        client.QueueQuery(new() { Category = PlayerItemCategories.Gear }, 0);
        Assert.True(client.Receive(Serializer.DeepClone(f.Service.Query(f.Player, client.Pump(200)!))));
        var entry = Assert.Single(client.CurrentPage!.Entries);
        Assert.Equal("game:gear-rusty", entry.AssetCode);
        var discovery = client.Select(entry.EntryId, "");
        var discovered = Serializer.DeepClone(f.Service.Detail(f.Player, discovery));
        Assert.Equal("", discovered.RejectionCode);
        Assert.Equal(gear.EquipmentRole, discovered.RoleCode);
        Assert.True(client.Receive(discovered));
        Assert.NotEmpty(client.CurrentDetail!.Comparison);
        // A later explicit-role request uses only what the server exposed in discovery.
        var request = client.Select(entry.EntryId, client.CurrentDetail.RoleCode);
        var compared = Serializer.DeepClone(f.Service.Detail(f.Player, request));
        Assert.Equal("", compared.RejectionCode);
        Assert.True(client.Receive(compared));
        Assert.NotEmpty(client.CurrentDetail!.Comparison);
    }

    [Theory]
    [InlineData(typeof(PlayerItemQueryRequest), "ProtocolVersion", 1)]
    [InlineData(typeof(PlayerItemQueryRequest), "Limit", 10)]
    [InlineData(typeof(PlayerItemActionRequest), "Quantity", 8)]
    [InlineData(typeof(PlayerItemActionRequest), "RequestSequence", 12)]
    [InlineData(typeof(PlayerItemDetailResult), "BuildRevision", 6)]
    [InlineData(typeof(PlayerItemDetailResult), "Actions", 9)]
    public void StableMemberNumbersAreExplicit(Type packet, string name, int tag)
    {
        var attribute = Assert.IsType<ProtoMemberAttribute>(Attribute.GetCustomAttribute(packet.GetProperty(name)!, typeof(ProtoMemberAttribute)));
        Assert.Equal(tag, attribute.Tag);
    }

    private sealed class Fixture
    {
        public readonly PlayerItemStoreDirectory Items;
        public readonly RpgPlayerStore Players = new();
        public readonly TestPlayer Player = new("p");
        public readonly PlayerItemNetworkService Service;
        public double Now;
        private readonly string id;
        public IPlayerItemStore Store => Items.ForPlayer("p");
        public Fixture(Func<string>? createEntryId = null, PlayerItemCategoryRegistry? categories = null)
        {
            Items = new(_ => new InMemoryPlayerItemStore(createEntryId, categories: categories));
            var talents = new TalentTreeCatalog();
            Service = new(Items, Players, new(), new(new GearComparisonService(TestGearDefinitions.Registry(),
                new(talents), new(new RpgModuleConfig(), talents))), new GearEquipmentService(TestGearDefinitions.Registry()), () => Now);
            id = Store.Insert("insert", PlayerItemQueryServiceTests.Item("a"), 10000, 0).EntryId;
            Service.Connect(Player);
        }
        public PlayerItemQueryRequest Query() => new() { ConnectionGeneration = 1, QueryId = 1 };
        public void Refresh() => Assert.Equal("", Service.Query(Player, Query()).RejectionCode);
        public string InsertGear(int requiredLevel, string displayName = "Test Sword", string category = PlayerItemCategories.Gear)
        {
            var instance = GearReadinessValidatorTests.Instance() with { RequiredLevel = requiredLevel };
            return Store.Insert("gear-" + Store.Revision, new(PlayerItemStorageKind.Unique, "game:test-sword",
                category, displayName, GearItemStateCodec.Encode(instance)), 1, Store.Revision).EntryId;
        }
        public PlayerItemActionRequest Action(long sequence) => Action(PlayerItemAction.Destroy, null, "", sequence);
        public PlayerItemActionRequest Action(PlayerItemAction action = PlayerItemAction.Destroy, string? entryId = null,
            string roleCode = "", long sequence = 1, long? expectedStoreRevision = null) => new()
        {
            ConnectionGeneration = 1, RequestId = "request-" + sequence, RequestSequence = sequence, Action = action,
            EntryId = entryId ?? id, RoleCode = roleCode, Quantity = 1,
            ExpectedStoreRevision = expectedStoreRevision ?? Store.Revision,
            ExpectedBuildRevision = Players.GetBuildRevision("p", Store.Equipment())
        };
    }

    private sealed class TestPlayer(string uid) : Vintagestory.Server.ServerPlayer(null!,
        new Vintagestory.Server.ServerWorldPlayerData { EntityPlayer = new EntityPlayer() })
    {
        public override string PlayerUID => uid;
        protected override void Init() { }
    }
}
