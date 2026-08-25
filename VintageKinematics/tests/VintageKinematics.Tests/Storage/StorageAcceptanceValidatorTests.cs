using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VintageKinematics.Storage.Acceptance;
using Xunit;

namespace VintageKinematics.Tests.Storage
{
    public class StorageAcceptanceValidatorTests
    {
        private readonly KineticStorageAcceptanceValidator validator = new();

        [Fact]
        public void Validate_AcceptsOrdinaryItem()
        {
            StorageAcceptanceResult result = validator.Validate(null, StorageTestStacks.Create("game:stick"), 1);

            Assert.True(result.Accepted);
        }

        [Fact]
        public void Validate_RejectsTransitioningItem()
        {
            ItemStack stack = StorageTestStacks.Create("game:fruit");
            stack.Collectible.TransitionableProps = new[] { new TransitionableProperties() };

            StorageAcceptanceResult result = validator.Validate(null, stack, 1);

            AssertRejected(result, StorageRejectionCodes.Transitioning);
        }

        [Theory]
        [InlineData("temperature")]
        [InlineData("timeFrozen")]
        public void Validate_RejectsTemperatureState(string attribute)
        {
            ItemStack stack = StorageTestStacks.Create("game:tool");
            stack.Attributes.SetFloat(attribute, 20f);

            StorageAcceptanceResult result = validator.Validate(null, stack, 1);

            AssertRejected(result, StorageRejectionCodes.Temperature);
        }

        [Fact]
        public void Validate_RejectsNestedItemStackAtAnyDepth()
        {
            ItemStack stack = StorageTestStacks.Create("game:container");
            TreeAttribute child = new TreeAttribute();
            child["contents"] = new ItemstackAttribute(StorageTestStacks.Create("game:stick"));
            stack.Attributes["nested"] = child;

            StorageAcceptanceResult result = validator.Validate(null, stack, 1);

            AssertRejected(result, StorageRejectionCodes.NestedStack);
        }

        [Fact]
        public void Validate_AcceptsInitializedBackpackWithOnlyEmptySlots()
        {
            ItemStack stack = StorageTestStacks.Create("game:backpack");
            TreeAttribute slots = new TreeAttribute();
            slots["slot-0"] = new ItemstackAttribute(null);
            TreeAttribute backpack = new TreeAttribute();
            backpack["slots"] = slots;
            stack.Attributes["backpack"] = backpack;

            StorageAcceptanceResult result = validator.Validate(null, stack, 1);

            Assert.True(result.Accepted);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Validate_AllowsOnlyEmptyHeldBags(bool empty)
        {
            ItemStack stack = new ItemStack(new TestHeldBag
            {
                Code = new AssetLocation("game:test-held-container"),
                Empty = empty
            });

            StorageAcceptanceResult result = validator.Validate(null, stack, 1);

            if (empty)
            {
                Assert.True(result.Accepted);
            }
            else
            {
                AssertRejected(result, StorageRejectionCodes.Backpack);
            }
        }

        [Fact]
        public void Validate_AcceptsEmptyLiquidContainer()
        {
            ItemStack stack = new ItemStack(new TestLiquidContainer
            {
                Code = new AssetLocation("game:bucket")
            });

            StorageAcceptanceResult result = validator.Validate(null, stack, 1);

            Assert.True(result.Accepted);
        }

        [Fact]
        public void Validate_RejectsLiquidContainerHoldingFluid()
        {
            ItemStack stack = new ItemStack(new TestLiquidContainer
            {
                Code = new AssetLocation("game:water-bucket"),
                CurrentLitres = 10f,
                Content = StorageTestStacks.Create("game:waterportion")
            });

            StorageAcceptanceResult result = validator.Validate(null, stack, 1);

            AssertRejected(result, StorageRejectionCodes.LiquidContainer);
        }

        [Fact]
        public void Validate_AppliesCodeBlacklist()
        {
            StorageAcceptanceRules rules = new StorageAcceptanceRules(
                blockedCodes: new[] { "game:forbidden" });
            KineticStorageAcceptanceValidator configured = new KineticStorageAcceptanceValidator(rules);

            StorageAcceptanceResult result = configured.Validate(
                null,
                StorageTestStacks.Create("game:forbidden"),
                1);

            AssertRejected(result, StorageRejectionCodes.Blacklisted);
        }

        [Fact]
        public void Validate_AppliesClassBlacklist()
        {
            StorageAcceptanceRules rules = new StorageAcceptanceRules(
                blockedClasses: new[] { nameof(TestBlockedItem) });
            KineticStorageAcceptanceValidator configured = new KineticStorageAcceptanceValidator(rules);
            ItemStack stack = new ItemStack(new TestBlockedItem
            {
                Code = new AssetLocation("game:blocked-class")
            });

            StorageAcceptanceResult result = configured.Validate(null, stack, 1);

            AssertRejected(result, StorageRejectionCodes.Blacklisted);
        }

        private static void AssertRejected(StorageAcceptanceResult result, string expectedCode)
        {
            Assert.False(result.Accepted);
            Assert.Equal(expectedCode, result.MessageLangCode);
        }

        private sealed class TestBlockedItem : Item
        {
        }

        private sealed class TestLiquidContainer : Item, ILiquidInterface
        {
            public float CurrentLitres { get; set; }
            public ItemStack Content { get; set; }
            public bool AllowHeldLiquidTransfer => true;
            public float CapacityLitres => 10f;
            public float TransferSizeLitres => 1f;
            public float GetCurrentLitres(ItemStack containerStack) => CurrentLitres;
            public float GetCurrentLitres(BlockPos pos) => 0f;
            public bool IsFull(ItemStack containerStack) => false;
            public bool IsFull(BlockPos pos) => false;
            public WaterTightContainableProps GetContentProps(ItemStack containerStack) => null;
            public WaterTightContainableProps GetContentProps(BlockPos pos) => null;
            public ItemStack GetContent(ItemStack containerStack) => Content;
            public ItemStack GetContent(BlockPos pos) => null;
        }

        private sealed class TestHeldBag : Item, IHeldBag
        {
            public bool Empty { get; set; }

            public bool IsEmpty(ItemStack bagstack) => Empty;
            public int GetQuantitySlots(ItemStack bagstack) => 4;
            public ItemStack[] GetContents(ItemStack bagstack, IWorldAccessor world) => [];
            public List<ItemSlotBagContent> GetOrCreateSlots(
                ItemStack bagstack,
                InventoryBase parentinv,
                int bagIndex,
                IWorldAccessor world) => [];
            public void Store(ItemStack bagstack, ItemSlotBagContent slot) { }
            public void Clear(ItemStack bagstack) { }
            public string GetSlotBgColor(ItemStack bagstack) => null;
            public override EnumItemStorageFlags GetStorageFlags(ItemStack bagstack) =>
                EnumItemStorageFlags.General;
            public TagSet GetStorageTags(ItemStack bagStack) => default;
        }
    }
}
