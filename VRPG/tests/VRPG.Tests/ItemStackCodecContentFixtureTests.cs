using System;
using System.Collections.Generic;
using VRPG.Inventory;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Xunit;

namespace VRPG.Tests;

/// <summary>
/// Runtime-state fixtures based on Vintage Story 1.22 assets and the audited 2026 creature packs:
/// Eidolon Unchained 1.9.0v, Monsterpack 1.1.10, and Fantasy Creatures Reborn 0.8.3.
/// Asset JSON is not copied into the test assembly; these fixtures model the stack state those assets use.
/// </summary>
public sealed class ItemStackCodecContentFixtureTests
{
    [Fact]
    public void AuditedVanillaAndAddonFixtureShapesRoundTripAcrossRegistryChanges()
    {
        foreach (ContentFixture fixture in Fixtures())
        {
            FixtureRegistry sourceRegistry = fixture.CreateRegistry(100);
            ItemStack source = fixture.CreateStack(sourceRegistry);
            EncodedItemStack encoded = ItemStackCodec.Encode(source);
            FixtureRegistry destinationRegistry = fixture.CreateRegistry(2_000);

            ItemStackMaterialization materialized = ItemStackCodec.Materialize(
                encoded.Payload,
                encoded.Quantity,
                destinationRegistry);

            Assert.True(materialized.Resolved, fixture.Name);
            Assert.NotNull(materialized.Stack);
            Assert.Equal(fixture.RootCode, materialized.Stack!.Collectible.Code.ToString());
            Assert.Equal(fixture.Quantity, materialized.Stack.StackSize);

            EncodedItemStack encodedAgain = ItemStackCodec.Encode(materialized.Stack);
            Assert.Equal(encoded.Payload.AssetKind, encodedAgain.Payload.AssetKind);
            Assert.Equal(encoded.Payload.AssetCode, encodedAgain.Payload.AssetCode);
            Assert.Equal(encoded.Payload.ExactState, encodedAgain.Payload.ExactState);
            Assert.Equal(encoded.Payload.EmbeddedReferences, encodedAgain.Payload.EmbeddedReferences);
        }
    }

    private static IEnumerable<ContentFixture> Fixtures()
    {
        yield return new ContentFixture(
            "Vanilla durable heated blade",
            "game:blade-long-iron",
            1,
            new[] { "game:blade-long-iron" },
            (stack, _) =>
            {
                stack.Attributes.SetInt("durability", 831);
                var temperature = new TreeAttribute();
                temperature.SetFloat("temperature", 684.5f);
                temperature.SetDouble("temperatureLastUpdate", 1_234.25);
                stack.Attributes["temperature"] = temperature;
                stack.Attributes.SetFloat("quality", 2.75f);
            });

        yield return new ContentFixture(
            "Vanilla perishable food",
            "game:bread-spelt-perfect",
            16,
            new[] { "game:bread-spelt-perfect" },
            (stack, _) =>
            {
                var perish = new TreeAttribute();
                perish.SetDouble("createdTotalHours", 100);
                perish.SetDouble("lastUpdatedTotalHours", 118.5);
                perish.SetFloat("transitionedHours", 2.25f);
                stack.Attributes["transitionstate"] = new TreeArrayAttribute(new[] { perish });
                stack.Attributes.SetFloat("spoilstate", 0.18f);
            });

        yield return new ContentFixture(
            "Vanilla backpack contents",
            "game:backpack-linen-normal",
            1,
            new[] { "game:backpack-linen-normal", "game:ingot-copper", "game:gear-temporal" },
            (stack, registry) =>
            {
                var slots = new TreeAttribute();
                slots.SetItemstack("slot-0", new ItemStack(registry.Item("game:ingot-copper"), 12));
                slots.SetItemstack("slot-1", new ItemStack(registry.Item("game:gear-temporal"), 1));
                var backpack = new TreeAttribute();
                backpack["slots"] = slots;
                stack.Attributes["backpack"] = backpack;
            });

        yield return new ContentFixture(
            "Fantasy Creatures dragon armor",
            "fantasycreatures:dragonarmor-body-ruby",
            1,
            new[] { "fantasycreatures:dragonarmor-body-ruby" },
            (stack, _) =>
            {
                stack.Attributes.SetInt("durability", 742);
                stack.Attributes.SetString("vrpg-fixture-source", "fantasycreaturesreborn_0_8_3");
                stack.Attributes.SetBytes("addon-opaque-state", new byte[] { 0xFA, 0xCE, 0x83 });
            });

        yield return new ContentFixture(
            "Fantasy Creatures dragon backpack",
            "fantasycreatures:dragonbackpack-ruby",
            1,
            new[]
            {
                "fantasycreatures:dragonbackpack-ruby",
                "fantasycreatures:dragonhide-ruby",
                "monsterpack:creature-zombieknight"
            },
            (stack, registry) =>
            {
                var slots = new TreeAttribute();
                slots.SetItemstack(
                    "slot-0",
                    new ItemStack(registry.Item("fantasycreatures:dragonhide-ruby"), 9));
                slots.SetItemstack(
                    "slot-1",
                    new ItemStack(registry.Item("monsterpack:creature-zombieknight"), 1));
                var backpack = new TreeAttribute();
                backpack["slots"] = slots;
                stack.Attributes["backpack"] = backpack;
            });

        yield return new ContentFixture(
            "Fantasy Creatures aggregate dragon hide",
            "fantasycreatures:dragonhide-scorched",
            64,
            new[] { "fantasycreatures:dragonhide-scorched" },
            (stack, _) => stack.Attributes.SetString("harvest-source", "dragon-scorched"));

        yield return new ContentFixture(
            "Monsterpack creature item",
            "monsterpack:creature-zombieknight",
            1,
            new[] { "monsterpack:creature-zombieknight" },
            (stack, _) => stack.Attributes.SetLong("captured-entity-id", 9_001));

        yield return new ContentFixture(
            "Eidolon Unchained creature item",
            "unchained:creature-unchained",
            32,
            new[] { "unchained:creature-unchained" },
            (stack, _) => stack.Attributes.SetString("origin", "eidolon-unchained"));
    }

    private sealed record ContentFixture(
        string Name,
        string RootCode,
        int Quantity,
        string[] ItemCodes,
        Action<ItemStack, FixtureRegistry> Configure)
    {
        public FixtureRegistry CreateRegistry(int firstId)
        {
            var items = new List<Item>();
            for (int index = 0; index < ItemCodes.Length; index++)
            {
                string code = ItemCodes[index];
                int maxStack = code.Contains("backpack", StringComparison.Ordinal)
                    || code.Contains("armor", StringComparison.Ordinal)
                    || code.Contains("blade", StringComparison.Ordinal)
                    || code.Contains("monsterpack:creature", StringComparison.Ordinal)
                    ? 1
                    : 64;
                items.Add(new Item(firstId + index)
                {
                    Code = new AssetLocation(code),
                    MaxStackSize = maxStack
                });
            }
            return new FixtureRegistry(items.ToArray(), Array.Empty<Block>());
        }

        public ItemStack CreateStack(FixtureRegistry registry)
        {
            var stack = new ItemStack(registry.Item(RootCode), Quantity);
            Configure(stack, registry);
            return stack;
        }
    }

    private sealed class FixtureRegistry : ICollectibleResolver
    {
        private readonly Item[] items;
        private readonly Block[] blocks;

        public FixtureRegistry(Item[] items, Block[] blocks)
        {
            this.items = items;
            this.blocks = blocks;
        }

        public Item Item(string code) => GetItem(new AssetLocation(code))
            ?? throw new InvalidOperationException("Fixture item is not registered: " + code);

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
