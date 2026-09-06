using System;
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
        public readonly PlayerItemStoreDirectory Items = new();
        public readonly RpgPlayerStore Players = new();
        public readonly TestPlayer Player = new("p");
        public readonly PlayerItemNetworkService Service;
        public double Now;
        private readonly string id;
        public Fixture()
        {
            var talents = new TalentTreeCatalog();
            Service = new(Items, Players, new(), new(new GearComparisonService(TestGearDefinitions.Registry(),
                new(talents), new(new RpgModuleConfig(), talents))), () => Now);
            id = Items.ForPlayer("p").Insert("insert", PlayerItemQueryServiceTests.Item("a"), 10000, 0).EntryId;
            Service.Connect(Player);
        }
        public PlayerItemQueryRequest Query() => new() { ConnectionGeneration = 1, QueryId = 1 };
        public PlayerItemActionRequest Action(long sequence = 1) => new() { ConnectionGeneration = 1,
            RequestId = "request-" + sequence, RequestSequence = sequence, Action = PlayerItemAction.Destroy,
            EntryId = id, Quantity = 1, ExpectedStoreRevision = 1, ExpectedBuildRevision = 1 };
    }

    private sealed class TestPlayer(string uid) : Vintagestory.Server.ServerPlayer(null!,
        new Vintagestory.Server.ServerWorldPlayerData { EntityPlayer = new EntityPlayer() })
    {
        public override string PlayerUID => uid;
        protected override void Init() { }
    }
}
