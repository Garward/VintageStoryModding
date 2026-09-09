using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using VRPG.Inventory.Migration;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.Server;

namespace VRPG.Tests;

// Native player/manager setup shared with the journaled Clear/Restore guard control.
internal sealed class NativeLegacyInventoryFixture
{
    public readonly Dictionary<string, LegacyInventory> Inventories;
    public readonly Vintagestory.API.Datastructures.OrderedDictionary<string, InventoryBase> Registered = new();
    public readonly VintageStoryLegacyInventorySource Source;
    public readonly List<string> Diagnostics = new();

    public NativeLegacyInventoryFixture()
    {
        var item = new NativeMutationFixture.ProbeItem { ItemId = 1, Code = new("game:legacy-clear-fixture"), MaxStackSize = 64 };
        var logger = PlayerDefenseServiceTests.Proxy.Create<ILogger>((name, args) =>
        {
            if (name == "Error") Diagnostics.Add(args!.Length == 2 && args[1] is object[] formatArgs
                ? string.Format((string)args[0]!, formatArgs) : string.Join(" ", args.Select(arg => arg?.ToString())));
            return null;
        });
        var world = PlayerDefenseServiceTests.Proxy.Create<IWorldAccessor>((name, _) => name switch
        { "GetItem" => item, "get_Logger" => logger, _ => null });
        var api = PlayerDefenseServiceTests.Proxy.Create<ICoreAPI>((name, _) => name switch
        { "get_World" => world, "get_Side" => EnumAppSide.Server, "get_Logger" => logger, _ => null });
        Inventories = new[] { "hotbar", "backpack", "character", "mouse", "craftinggrid" }
            .ToDictionary(name => name, name => new LegacyInventory(name, api));
        var player = (NativeClearCommandFixture.BroadcastPlayer)RuntimeHelpers.GetUninitializedObject(typeof(NativeClearCommandFixture.BroadcastPlayer));
        var data = ServerWorldPlayerData.CreateNew("player", "player");
        data.EntityPlayer = new EntityPlayer { World = world, Api = api };
        AccessTools.Field(typeof(ServerPlayer), "worlddata").SetValue(player, data);
        foreach (var inventory in Inventories.Values) Registered.Add(inventory.InventoryID, inventory);
        AccessTools.Field(typeof(ServerPlayer), "inventoryMgr").SetValue(player, new LegacyManager(Registered, player));
        Inventories["hotbar"][0].Itemstack = new ItemStack(item, 7);
        Inventories["mouse"][0].Itemstack = new ItemStack(item, 3);
        Source = new VintageStoryLegacyInventorySource(player);
    }

    public void MakeUnavailable(string reason)
    {
        switch (reason)
        {
            case "missing-one": Registered.Remove("character-player"); break;
            case "missing-all": Registered.Clear(); break;
            case "paused": Inventories["hotbar"].Paused = true; break;
            case "throwing": Inventories["character"].ThrowOnRead = true; break;
            case "external":
                var container = new LegacyInventory("chest", Inventories["hotbar"].Api);
                container.openedByPlayerGUIds.Add("player");
                Registered.Add(container.InventoryID, container);
                break;
            default: throw new ArgumentException(reason);
        }
    }

    public void MakeAvailable()
    {
        Registered.Clear();
        foreach (var inventory in Inventories.Values)
        {
            inventory.Paused = false;
            inventory.ThrowOnRead = false;
            Registered.Add(inventory.InventoryID, inventory);
        }
    }

    public int Quantity => Inventories.Values.Sum(inventory => inventory.Quantity);

    internal sealed class LegacyInventory : InventoryBasePlayer
    {
        private readonly ItemSlot[] slots;
        public bool Paused;
        public bool ThrowOnRead;
        public Action? AfterModification;
        public int Quantity => slots.Sum(slot => slot.StackSize);
        public LegacyInventory(string name, ICoreAPI api) : base(name, "player", null!)
        {
            Api = api;
            slots = GenEmptySlots(4);
            InvNetworkUtil = PlayerDefenseServiceTests.Proxy.Create<IInventoryNetworkUtil>((name, _) =>
                name == "get_PauseInventoryUpdates" ? Paused : null);
        }
        public override int Count => ThrowOnRead ? throw new InvalidOperationException("private native inspection detail") : slots.Length;
        public override ItemSlot this[int i] { get => slots[i]; set => slots[i] = value; }
        public override void FromTreeAttributes(ITreeAttribute tree) { }
        public override void ToTreeAttributes(ITreeAttribute tree) { }
        public override void DidModifyItemSlot(ItemSlot slot, ItemStack extractedStack = null!) => AfterModification?.Invoke();
    }

    private sealed class LegacyManager(Vintagestory.API.Datastructures.OrderedDictionary<string, InventoryBase> inventories, IPlayer player)
        : ServerPlayerInventoryManager(inventories, player, null!)
    {
        public override void BroadcastHotbarSlot() { }
    }
}
