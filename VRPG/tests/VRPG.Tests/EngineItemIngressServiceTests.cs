using VRPG.Inventory;
using Vintagestory.API.Common;
using Xunit;

namespace VRPG.Tests;

public sealed class EngineItemIngressServiceTests
{
    [Theory]
    [InlineData("temperature")]
    [InlineData("transitionstate")]
    [InlineData("perishable")]
    public void EvolvingEngineStateIsFrozenWithoutLosingExactSource(string evolvingState)
    {
        var directory = new PlayerItemStoreDirectory();
        var service = new EngineItemIngressService(directory);
        var item = new Item { Code = new AssetLocation("game:evolving"), MaxStackSize = 64 };
        var stack = new ItemStack(item, 3);
        if (evolvingState == "perishable")
            item.TransitionableProps = new[] { new TransitionableProperties { Type = EnumTransitionType.Perish } };
        else
            stack.Attributes[evolvingState] = new Vintagestory.API.Datastructures.TreeAttribute();
        byte[] exactState = ItemStackCodec.Encode(stack).Payload.ExactState;

        Assert.Equal(EngineItemIngressDisposition.Accepted, service.Route("player", stack, "grant").Disposition);

        IPlayerItemStore store = directory.ForPlayer("player");
        Assert.Empty(store.Query(Query()).Entries);
        var entry = Assert.Single(store.Query(Query() with { Location = PlayerItemLocation.Unresolved }).Entries);
        Assert.True(store.TryGet(entry.EntryId, out var detail));
        Assert.Equal(PlayerItemStatePolicy.FrozenInRecovery, detail!.Prototype.StatePolicy);
        Assert.Equal(exactState, detail.Prototype.ExactState);
        Assert.Equal(3, entry.Quantity);
        Assert.Equal(3, stack.StackSize);
        Assert.False(store.Reserve("consume", new[] { new PlayerItemAmount(entry.EntryId, 1) }, store.Revision).Success);
    }

    [Fact]
    public void RoutesWholeEngineStackWithoutMutatingSource()
    {
        var directory = new PlayerItemStoreDirectory(_ =>
            new InMemoryPlayerItemStore(createEntryId: () => "entry-1"));
        var service = new EngineItemIngressService(directory);
        var item = new Item { Code = new AssetLocation("game:metal-scraps"), MaxStackSize = 64 };
        var stack = new ItemStack(item, 37);

        EngineItemIngressResult result = service.Route("player-a", stack, "pickup-1");

        Assert.Equal(EngineItemIngressDisposition.Accepted, result.Disposition);
        Assert.Equal(37, stack.StackSize);
        PlayerItemPage page = directory.ForPlayer("player-a").Query(Query());
        Assert.Single(page.Entries);
        Assert.Equal(37, page.Entries[0].Quantity);
        Assert.Equal(PlayerItemCategories.Miscellaneous, page.Entries[0].Category);
    }

    [Fact]
    public void ConsecutiveEngineGrantsAggregateWithDistinctRequests()
    {
        var directory = new PlayerItemStoreDirectory(_ =>
            new InMemoryPlayerItemStore(createEntryId: () => "entry-1"));
        var service = new EngineItemIngressService(directory);
        var item = new Item { Code = new AssetLocation("game:flax-fibers"), MaxStackSize = 64 };
        var stack = new ItemStack(item, 12);

        Assert.Equal(EngineItemIngressDisposition.Accepted,
            service.Route("player-a", stack, "pickup-1").Disposition);
        Assert.Equal(EngineItemIngressDisposition.Accepted,
            service.Route("player-a", stack, "pickup-2").Disposition);

        PlayerItemPage page = directory.ForPlayer("player-a").Query(Query());
        Assert.Equal(24, Assert.Single(page.Entries).Quantity);
    }

    [Fact]
    public void RejectsAtomicallyWhenCanonicalStoreIsFull()
    {
        var directory = new PlayerItemStoreDirectory(_ => new InMemoryPlayerItemStore(
            createEntryId: () => "entry-1",
            capacity: new PlayerItemCapacity(1, 1, 10)));
        var service = new EngineItemIngressService(directory);
        var item = new Item { Code = new AssetLocation("game:flax-fibers"), MaxStackSize = 64 };
        var stack = new ItemStack(item, 11);

        EngineItemIngressResult result = service.Route("player-a", stack, "pickup-1");

        Assert.Equal(EngineItemIngressDisposition.Rejected, result.Disposition);
        Assert.Equal(PlayerItemMutationRejection.QuantityOverflow, result.Rejection);
        Assert.Equal(11, stack.StackSize);
        Assert.Empty(directory.ForPlayer("player-a").Query(Query()).Entries);
    }

    [Fact]
    public void InvalidOrUnownedInputFallsThrough()
    {
        var service = new EngineItemIngressService(new PlayerItemStoreDirectory());

        Assert.Equal(EngineItemIngressDisposition.PassThrough,
            service.Route("", null, "").Disposition);
    }

    private static PlayerItemQuery Query() => new("", "", 0, 100);
}
