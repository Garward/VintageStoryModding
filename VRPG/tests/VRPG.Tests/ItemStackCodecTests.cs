using System;
using VRPG.Inventory;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Xunit;

namespace VRPG.Tests;

public sealed class ItemStackCodecTests
{
    [Fact]
    public void DescribeFreezesNestedEvolvingStateWithoutUpdatingItsClock()
    {
        var source = new ItemStack(TestItem(1, "game:container", 1), 1);
        var nested = new ItemStack(TestItem(2, "game:food", 64), 4);
        var temperature = new TreeAttribute();
        temperature.SetDouble("temperatureLastUpdate", 123.5);
        nested.Attributes["temperature"] = temperature;
        var contents = new TreeAttribute();
        contents["food"] = new ItemstackAttribute(nested);
        source.Attributes["contents"] = new TreeArrayAttribute(new[] { contents });

        var description = ItemStackCodec.Describe(source);

        Assert.Equal(PlayerItemStatePolicy.FrozenInRecovery, description.StatePolicy);
        Assert.Equal(123.5, temperature.GetDouble("temperatureLastUpdate"));
        Assert.Equal(4, nested.StackSize);
    }

    [Fact]
    public void DescribeRefusesToAssumeDynamicTransitionOverridesAreStatic()
    {
        var stack = new ItemStack(new DynamicTransitionItem
        {
            Code = new AssetLocation("addon:evolving"), MaxStackSize = 64
        }, 1);

        Assert.Equal(PlayerItemStatePolicy.FrozenInRecovery, ItemStackCodec.Describe(stack).StatePolicy);
    }

    private sealed class DynamicTransitionItem : Item
    {
        public override TransitionableProperties[] GetTransitionableProperties(
            IWorldAccessor world, ItemStack itemstack, Vintagestory.API.Common.Entities.Entity forEntity) =>
            throw new InvalidOperationException("Inspection must not invoke a stateful engine callback.");
    }

    [Fact]
    public void ItemRoundTripUsesStableCodeAndPreservesSynchronizedAttributes()
    {
        Item item = TestItem(73, "addon:charged-blade", maxStack: 1);
        var source = new ItemStack(item, 1);
        source.Attributes.SetInt("durability", 417);
        source.Attributes.SetString("affix", "storm");
        EncodedItemStack encoded = ItemStackCodec.Encode(source);
        var resolver = new TestResolver(item, null);

        ItemStackMaterialization result = ItemStackCodec.Materialize(encoded.Payload, encoded.Quantity, resolver);

        Assert.True(result.Resolved);
        Assert.NotNull(result.Stack);
        Assert.Equal("addon:charged-blade", encoded.Payload.AssetCode);
        Assert.Equal(PlayerItemAssetKind.Item, encoded.Payload.AssetKind);
        Assert.Equal(1, result.Stack!.StackSize);
        Assert.Equal(417, result.Stack.Attributes.GetInt("durability"));
        Assert.Equal("storm", result.Stack.Attributes.GetString("affix"));
        Assert.Equal(item.ItemId, result.Stack.Id);
    }

    [Fact]
    public void BlockRoundTripResolvesCurrentRegistryIdInsteadOfPersistedId()
    {
        Block oldBlock = TestBlock(51, "game:slab-granite");
        var source = new ItemStack(oldBlock, 64);
        EncodedItemStack encoded = ItemStackCodec.Encode(source);
        Block currentBlock = TestBlock(912, "game:slab-granite");

        ItemStackMaterialization result = ItemStackCodec.Materialize(
            encoded.Payload,
            encoded.Quantity,
            new TestResolver(null, currentBlock));

        Assert.True(result.Resolved);
        Assert.Equal(912, result.Stack!.Id);
        Assert.Equal(64, result.Stack.StackSize);
    }

    [Fact]
    public void MissingAddonAssetReturnsRecoverablePayloadWithoutDiscardingState()
    {
        Item item = TestItem(7, "missingmod:relic", maxStack: 1);
        var source = new ItemStack(item, 1);
        source.Attributes.SetBytes("opaque", new byte[] { 9, 4, 2 });
        EncodedItemStack encoded = ItemStackCodec.Encode(source);

        ItemStackMaterialization result = ItemStackCodec.Materialize(
            encoded.Payload,
            1,
            new TestResolver(Array.Empty<Item>(), Array.Empty<Block>()));

        Assert.False(result.Resolved);
        Assert.Null(result.Stack);
        Assert.Equal(ItemStackMaterializationFailure.MissingAsset, result.Failure);
        Assert.Equal(encoded.Payload.AssetCode, result.RecoverablePayload.AssetCode);
        Assert.Equal(encoded.Payload.ExactState, result.RecoverablePayload.ExactState);
        Assert.NotSame(encoded.Payload.ExactState, result.RecoverablePayload.ExactState);
    }

    [Fact]
    public void TemporaryAttributesAreIntentionallyNotPersisted()
    {
        Item item = TestItem(4, "game:tool", maxStack: 1);
        var source = new ItemStack(item, 1);
        source.Attributes.SetString("saved", "yes");
        source.TempAttributes.SetString("render-cache", "no");

        EncodedItemStack encoded = ItemStackCodec.Encode(source);
        ItemStack stack = ItemStackCodec.Materialize(
            encoded.Payload,
            1,
            new TestResolver(item, null)).Stack!;

        Assert.Equal("yes", stack.Attributes.GetString("saved"));
        Assert.False(stack.TempAttributes.HasAttribute("render-cache"));
    }

    [Fact]
    public void DescribeSuppliesClassifierFactsWithoutDisplayText()
    {
        Item item = TestItem(2, "game:pickaxe", maxStack: 1);
        item.Tool = EnumTool.Pickaxe;

        PlayerItemClassificationContext context = ItemStackCodec.Describe(new ItemStack(item));

        Assert.Equal("game:pickaxe", context.AssetCode);
        Assert.True(context.IsTool);
        Assert.False(context.IsConsumable);
    }

    [Fact]
    public void NestedStackAttributesRemapDynamicRegistryIdsRecursively()
    {
        Item oldContainer = TestItem(10, "game:container", maxStack: 1);
        Item oldGem = TestItem(72, "addon:gem", maxStack: 64);
        Item oldDust = TestItem(73, "addon:dust", maxStack: 64);
        var nestedGem = new ItemStack(oldGem, 3);
        nestedGem.Attributes.SetItemstack("deeper", new ItemStack(oldDust, 5));
        var source = new ItemStack(oldContainer, 1);
        source.Attributes.SetItemstack("content", nestedGem);
        EncodedItemStack encoded = ItemStackCodec.Encode(source);

        Item currentContainer = TestItem(110, "game:container", maxStack: 1);
        Item currentGem = TestItem(720, "addon:gem", maxStack: 64);
        Item currentDust = TestItem(730, "addon:dust", maxStack: 64);
        var resolver = new TestResolver(new[] { currentContainer, currentGem, currentDust }, Array.Empty<Block>());

        ItemStackMaterialization result = ItemStackCodec.Materialize(encoded.Payload, 1, resolver);

        Assert.True(result.Resolved);
        ItemStack gem = result.Stack!.Attributes.GetItemstack("content");
        ItemStack dust = gem.Attributes.GetItemstack("deeper");
        Assert.Equal(720, gem.Id);
        Assert.Equal(3, gem.StackSize);
        Assert.Equal(730, dust.Id);
        Assert.Equal(5, dust.StackSize);

        EncodedItemStack encodedAgain = ItemStackCodec.Encode(result.Stack);
        Assert.Equal(encoded.Payload.ExactState, encodedAgain.Payload.ExactState);
        Assert.Equal(encoded.Payload.EmbeddedReferences, encodedAgain.Payload.EmbeddedReferences);
    }

    [Fact]
    public void MissingNestedAssetKeepsWholeTopLevelPayloadRecoverable()
    {
        Item container = TestItem(10, "game:container", maxStack: 1);
        Item addonItem = TestItem(72, "missingmod:content", maxStack: 64);
        var source = new ItemStack(container, 1);
        source.Attributes.SetItemstack("content", new ItemStack(addonItem, 2));
        EncodedItemStack encoded = ItemStackCodec.Encode(source);

        ItemStackMaterialization result = ItemStackCodec.Materialize(
            encoded.Payload,
            1,
            new TestResolver(new[] { container }, Array.Empty<Block>()));

        Assert.False(result.Resolved);
        Assert.Null(result.Stack);
        Assert.Equal(ItemStackMaterializationFailure.MissingEmbeddedAsset, result.Failure);
        Assert.Equal("game:container", result.RecoverablePayload.AssetCode);
        Assert.Equal(encoded.Payload.ExactState, result.RecoverablePayload.ExactState);
    }

    [Fact]
    public void MaterializationRejectsQuantityAboveEngineStackLimit()
    {
        Item item = TestItem(7, "game:material", maxStack: 64);
        EncodedItemStack encoded = ItemStackCodec.Encode(new ItemStack(item, 64));

        ItemStackMaterialization result = ItemStackCodec.Materialize(
            encoded.Payload,
            65,
            new TestResolver(item, null));

        Assert.False(result.Resolved);
        Assert.Equal(ItemStackMaterializationFailure.QuantityExceedsEngineLimit, result.Failure);
    }

    private static Item TestItem(int id, string code, int maxStack)
    {
        return new Item(id)
        {
            Code = new AssetLocation(code),
            MaxStackSize = maxStack
        };
    }

    private static Block TestBlock(int id, string code)
    {
        return new Block
        {
            BlockId = id,
            Code = new AssetLocation(code),
            MaxStackSize = 64
        };
    }

    private sealed class TestResolver : ICollectibleResolver
    {
        private readonly Item[] items;
        private readonly Block[] blocks;

        public TestResolver(Item? item, Block? block)
            : this(item == null ? Array.Empty<Item>() : new[] { item },
                block == null ? Array.Empty<Block>() : new[] { block })
        {
        }

        public TestResolver(Item[] items, Block[] blocks)
        {
            this.items = items;
            this.blocks = blocks;
        }

        public Item? GetItem(AssetLocation code)
        {
            foreach (Item item in items)
            {
                if (item.Code.Equals(code)) return item;
            }
            return null;
        }

        public Block? GetBlock(AssetLocation code)
        {
            foreach (Block block in blocks)
            {
                if (block.Code.Equals(code)) return block;
            }
            return null;
        }
    }
}
