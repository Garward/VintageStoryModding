using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using VRPG.Inventory.Migration;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.Server;
using Xunit;

namespace VRPG.Tests;

[Collection("EngineItemBirth")]
public sealed class NativeClearMigrationCompatibilityTests
{
    [Fact]
    public void HealthyJournaledSourceClearAndRestoreRemainAvailableUnderRecoveryCustody()
    {
        using var scope = new NativeMutationScope("recovery");
        var item = new NativeMutationFixture.ProbeItem { ItemId = 1, Code = new("game:legacy-clear-fixture"), MaxStackSize = 64 };
        var world = PlayerDefenseServiceTests.Proxy.Create<IWorldAccessor>((name, _) => name == "GetItem" ? item : null);
        var api = PlayerDefenseServiceTests.Proxy.Create<ICoreAPI>((name, _) => name switch
        { "get_World" => world, "get_Side" => EnumAppSide.Server, _ => null });
        var inventories = new[] { "hotbar", "backpack", "character", "mouse", "craftinggrid" }
            .ToDictionary(name => name, name => new LegacyInventory(name, api));
        var player = (NativeClearCommandFixture.BroadcastPlayer)RuntimeHelpers.GetUninitializedObject(typeof(NativeClearCommandFixture.BroadcastPlayer));
        var data = ServerWorldPlayerData.CreateNew("player", "player");
        data.EntityPlayer = new EntityPlayer { World = world, Api = api };
        AccessTools.Field(typeof(ServerPlayer), "worlddata").SetValue(player, data);
        var ordered = new Vintagestory.API.Datastructures.OrderedDictionary<string, InventoryBase>();
        foreach (var inventory in inventories.Values) ordered.Add(inventory.InventoryID, inventory);
        AccessTools.Field(typeof(ServerPlayer), "inventoryMgr").SetValue(player, new LegacyManager(ordered, player));
        inventories["hotbar"][0].Itemstack = new ItemStack(item, 7);
        inventories["mouse"][0].Itemstack = new ItemStack(item, 3);
        var source = new VintageStoryLegacyInventorySource(player);
        var inspection = source.Inspect();
        Assert.Empty(inspection.BlockingReasons);
        Assert.Equal(2, inspection.Slots.Length);
        Assert.True(source.Clear(inspection.Slots).Success);
        Assert.Empty(source.Inspect().Slots);
        Assert.True(source.Restore(inspection.Slots).Success);
        Assert.Equal(LegacyInventoryState.MatchesSnapshot, source.CompareTo(inspection.Slots));
        Assert.Equal(7, inventories["hotbar"][0].StackSize);
        Assert.Equal(3, inventories["mouse"][0].StackSize);
        inventories["hotbar"].Clear();
        Assert.Equal(7, inventories["hotbar"][0].StackSize);
    }

    private sealed class LegacyInventory : InventoryBasePlayer
    {
        private readonly ItemSlot[] slots;
        public LegacyInventory(string name, ICoreAPI api) : base(name, "player", null!)
        { Api = api; slots = GenEmptySlots(4); }
        public override int Count => slots.Length;
        public override ItemSlot this[int i] { get => slots[i]; set => slots[i] = value; }
        public override void FromTreeAttributes(ITreeAttribute tree) { }
        public override void ToTreeAttributes(ITreeAttribute tree) { }
        public override void DidModifyItemSlot(ItemSlot slot, ItemStack extractedStack = null!) { }
    }

    private sealed class LegacyManager(Vintagestory.API.Datastructures.OrderedDictionary<string, InventoryBase> inventories, IPlayer player)
        : ServerPlayerInventoryManager(inventories, player, null!)
    {
        public override void BroadcastHotbarSlot() { }
    }
}
