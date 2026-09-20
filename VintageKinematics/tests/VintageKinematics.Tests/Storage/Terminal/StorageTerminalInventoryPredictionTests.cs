using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using VintageKinematics.Gui.Storage;
using Xunit;

namespace VintageKinematics.Tests.Storage.Terminal
{
    public class StorageTerminalInventoryPredictionTests
    {
        [Theory]
        [InlineData(16)]
        [InlineData(80)]
        public void WithdrawIntoBagDoesNotRebuildContentsAndCanRollback(int quantity)
        {
            var (api, inventory) = CreateClient();
            ItemSlot first = inventory[1];
            var prediction = StorageTerminalInventoryPrediction.WithdrawStack(
                api, CreateStack("game:stone"), quantity);

            Assert.Equal(quantity, prediction.Moved);
            Assert.Same(first, inventory[1]);
            Assert.Equal(quantity, inventory.Skip(1).Sum(slot => slot.StackSize));
            Assert.Equal(quantity, inventory[0].Itemstack.Attributes.GetInt("stored"));

            prediction.Rollback();

            Assert.All(inventory.Skip(1), slot => Assert.True(slot.Empty));
            Assert.Equal(0, inventory[0].Itemstack.Attributes.GetInt("stored"));
        }

        [Fact]
        public void RollbackSkipsSlotsReplacedByInventoryUpdate()
        {
            var (api, inventory) = CreateClient();
            var prediction = StorageTerminalInventoryPrediction.WithdrawStack(
                api, CreateStack("game:stone"), 16);
            ItemSlot detached = inventory[1];
            inventory.RebuildContents();
            inventory[1].Itemstack = CreateStack("game:stone", 7);

            prediction.Rollback();

            Assert.Equal(7, inventory[1].StackSize);
            Assert.Equal(16, detached.StackSize);
        }

        [Fact]
        public void ConfirmKeepsPredictedItemsAndRollbackPreservesNewerChanges()
        {
            var (api, inventory) = CreateClient();
            var prediction = StorageTerminalInventoryPrediction.WithdrawStack(
                api, CreateStack("game:stone"), 16);
            prediction.Confirm();
            prediction.Rollback();
            Assert.Equal(16, inventory[1].StackSize);

            inventory[1].Itemstack = null;
            prediction = StorageTerminalInventoryPrediction.WithdrawStack(
                api, CreateStack("game:stone"), 16);
            inventory[1].Itemstack.StackSize = 9;
            prediction.Rollback();
            Assert.Equal(9, inventory[1].StackSize);
        }

        private static (ICoreClientAPI, RebuildingBagInventory) CreateClient()
        {
            var inventory = new RebuildingBagInventory();
            var manager = Proxy<IPlayerInventoryManager>((method, args) => method.Name switch
            {
                "get_InventoriesOrdered" => new InventoryBase[] { inventory },
                "GetBestSuitedSlot" => inventory.Skip(1).FirstOrDefault(slot =>
                    slot.Empty && !((List<ItemSlot>)args[3]).Contains(slot)),
                _ => null
            });
            var player = (TestPlayer)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(TestPlayer));
            player.TestInventoryManager = manager;
            var world = Proxy<IClientWorldAccessor>((method, _) =>
                method.Name == "get_Player" ? player : null);
            var api = Proxy<ICoreClientAPI>((method, _) =>
                method.Name == "get_World" ? world : null);
            inventory.Api = api;
            return (api, inventory);
        }

        private static ItemStack CreateStack(string code, int quantity = 1)
        {
            return new ItemStack(new TestItem
            {
                Code = new AssetLocation(code),
                MaxStackSize = 64
            }, quantity);
        }

        private static T Proxy<T>(System.Func<MethodInfo, object[], object> handler) where T : class
        {
            T proxy = DispatchProxy.Create<T, InterfaceProxy>();
            ((InterfaceProxy)(object)proxy).Handler = handler;
            return proxy;
        }

        private sealed class TestPlayer : Vintagestory.Client.NoObf.ClientPlayer, IClientPlayer
        {
            private TestPlayer() : base(null) { }
            public IPlayerInventoryManager TestInventoryManager { get; set; }
            IPlayerInventoryManager IPlayer.InventoryManager => TestInventoryManager;
            string IPlayer.PlayerUID => "test";
        }

        public class InterfaceProxy : DispatchProxy
        {
            public System.Func<MethodInfo, object[], object> Handler { get; set; }
            protected override object Invoke(MethodInfo method, object[] args) =>
                Handler(method, args) ?? (method.ReturnType.IsValueType && method.ReturnType != typeof(void)
                    ? Activator.CreateInstance(method.ReturnType) : null);
        }

        private sealed class TestItem : Item
        {
            public override bool Equals(
                ItemStack thisStack,
                ItemStack otherStack,
                params string[] ignoredAttributes)
            {
                return otherStack?.Collectible?.Code?.Equals(Code) == true;
            }
        }

        // Models vanilla's save-content-into-bag and rebuild-on-bag-modification behavior.
        // Keep the real ItemSlot transfer and InventoryBase notification/ownership checks.
        private sealed class RebuildingBagInventory : InventoryBasePlayer
        {
            private readonly ItemSlot[] slots = new ItemSlot[3];
            public RebuildingBagInventory() : base("backpack", "test", null)
            {
                slots[0] = new ItemSlot(this)
                {
                    Itemstack = CreateStack("game:backpack")
                };
                RebuildContents();
            }
            public override int Count => slots.Length;
            public override ItemSlot this[int index]
            {
                get => slots[index];
                set => slots[index] = value;
            }
            public void RebuildContents()
            {
                for (int i = 1; i < Count; i++)
                    slots[i] = new ItemSlotBagContent(this, 0, i - 1, EnumItemStorageFlags.General);
            }
            public override void OnItemSlotModified(ItemSlot slot)
            {
                if (slot is ItemSlotBagContent)
                    slots[0].Itemstack.Attributes.SetInt("stored", slots.Skip(1).Sum(s => s.StackSize));
                else
                    RebuildContents();
            }
            public override void FromTreeAttributes(ITreeAttribute tree) { }
            public override void ToTreeAttributes(ITreeAttribute tree) { }
        }
    }
}
