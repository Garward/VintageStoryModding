using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.Server;

namespace VRPG.Tests;

// Real native handlers/managers/slots; controlled downstream callbacks avoid a networked world.
internal sealed class NativeInventoryGuardFixture
{
    public ServerMain Server { get; } = (ServerMain)RuntimeHelpers.GetUninitializedObject(typeof(ServerMain));
    public ServerPlayer Player { get; } = (ServerPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ServerPlayer));
    public ConnectedClient Client { get; } = new(1);
    public ProbeInventory Inventory { get; } = new("test-player");
    public ProbeInventory Hotbar { get; } = new("hotbar-player");
    public ProbeCursor Cursor { get; } = new();
    public ProbeItem Item { get; } = new() { ItemId = 0, Code = new AssetLocation("game:guard-test"), MaxStackSize = 64 };
    public ProbeEvents Events { get; }

    public NativeInventoryGuardFixture(object native)
    {
        var data = new ServerWorldPlayerData { GameMode = EnumGameMode.Creative, EntityPlayer = new EntityPlayer() };
        AccessTools.Field(typeof(ServerWorldPlayerData), "PlayerUID").SetValue(data, "player");
        AccessTools.Field(typeof(ServerWorldPlayerData), "connected").SetValue(data, true);
        AccessTools.Field(typeof(ServerPlayer), "worlddata").SetValue(Player, data);
        var inventories = new Vintagestory.API.Datastructures.OrderedDictionary<string, InventoryBase>
        {
            [Inventory.InventoryID] = Inventory, [Hotbar.InventoryID] = Hotbar, [Cursor.InventoryID] = Cursor
        };
        AccessTools.Field(typeof(ServerPlayer), "inventoryMgr").SetValue(Player,
            new ServerPlayerInventoryManager(inventories, Player, Server));
        AccessTools.Field(typeof(ServerSystem), "server").SetValue(native, Server);
        AccessTools.Field(typeof(ServerMain), "totalUnpausedTime").SetValue(Server, Stopwatch.StartNew());
        Server.Config = (ServerConfig)RuntimeHelpers.GetUninitializedObject(typeof(ServerConfig));
        Server.Items = new List<Item> { Item };
        Events = new ProbeEvents(Server);
        Server.EventManager = Events;
        Client.Player = Player;
        Client.SentPlayerUid = "spoofed-vanilla";
        var api = PlayerDefenseServiceTests.Proxy.Create<ICoreAPI>((name, _) => name switch
        {
            "get_World" => Server,
            "get_Side" => EnumAppSide.Server,
            _ => null
        });
        foreach (var inventory in inventories.Values) inventory.Api = api;
        Inventory.InvNetworkUtil = new ProbeNetwork(Inventory);
        Inventory[0].Itemstack = new ItemStack(Item, 7);
        Hotbar[0].Itemstack = new ItemStack(Item, 7);
        Cursor[0].Itemstack = new ItemStack(Item, 5);
    }

    public Packet_Client Packet(string method) => method switch
    {
        "HandleSelectedHotbarSlot" => new() { SelectedHotbarSlot = new Packet_SelectedHotbarSlot { SlotNumber = 1 } },
        "HandleActivateInventorySlot" => new() { Id = 7, ActivateInventorySlot = new Packet_ActivateInventorySlot { TargetInventoryId = "test-player", TargetSlot = 0 } },
        "HandleMoveItemstack" => new() { Id = 8, MoveItemstack = new Packet_MoveItemstack { SourceInventoryId = "test-player", TargetInventoryId = "test-player", SourceSlot = 0, TargetSlot = 1 } },
        "HandleFlipItemStacks" => new() { Id = 9, Flipitemstacks = new Packet_FlipItemstacks { SourceInventoryId = "test-player", TargetInventoryId = "test-player", SourceSlot = 0, TargetSlot = 1 } },
        "HandleCreateItemstack" => new() { CreateItemstack = new Packet_CreateItemstack { TargetInventoryId = "test-player", TargetSlot = 1,
            Itemstack = new Packet_ItemStack { ItemId = 0, ItemClass = (int)EnumItemClass.Item, StackSize = 4 } } },
        "HandleInvOpenClose" => new() { InvOpenedClosed = new Packet_InvOpenClose { InventoryId = "test-player", Opened = 1 } },
        "HandleToolMode" => new() { ToolMode = new Packet_ToolMode { Face = 0, Mode = 3 } },
        "HandleHandInteraction" => new() { HandInteraction = new Packet_ClientHandInteraction { InventoryId = "test-player", SlotId = 0,
            UseType = (int)EnumHandInteract.HeldItemInteract, MouseButton = 2, OnBlockFace = 0, EnumHandInteract = 0 } },
        _ => throw new ArgumentOutOfRangeException(nameof(method))
    };

    internal sealed class ProbeInventory(string id) : InventoryGeneric(12, id, null)
    {
        public int OpenCalls;
        public int ModifiedCalls;
        public override bool HasOpened(IPlayer player) => true;
        public override object Open(IPlayer player) { OpenCalls++; return null!; }
        public override object Close(IPlayer player) => null!;
        public override void DidModifyItemSlot(ItemSlot slot, ItemStack extractedStack = null!) => ModifiedCalls++;
    }

    internal sealed class ProbeNetwork(InventoryBase inventory) : InventoryNetworkUtil(inventory, null)
    {
        public override void HandleClientPacket(IPlayer player, int id, Packet_Client packet)
        {
            // Models an arbitrary downstream transfer callback, with real source/destination slots.
            inv[1].Itemstack = inv[0].Itemstack;
            inv[0].Itemstack = null;
        }
    }

    internal sealed class ProbeCursor() : InventoryPlayerMouseCursor("mouse-player", null)
    {
        public int Drops;
        public override void DropAll(Vec3d position, int maxStackSize = 0) { Drops++; this[0].Itemstack = null; }
    }

    internal sealed class ProbeEvents(ServerMain server) : CoreServerEventManager(server, new ServerEventManager(server))
    {
        public int SelectionCalls;
        public override bool TriggerBeforeActiveSlotChanged(IServerPlayer player, int from, int to)
        {
            SelectionCalls++;
            throw new SelectionCallbackReached(); // Native event reached; stop before unrelated network broadcast.
        }
    }
    internal sealed class SelectionCallbackReached : Exception;

    internal sealed class ProbeItem : Item
    {
        public int ToolCalls;
        public int UseCalls;
        public override void SetToolMode(ItemSlot slot, IPlayer player, BlockSelection selection, int mode) => ToolCalls++;
        public override void OnHeldUseStart(ItemSlot slot, EntityAgent entity, BlockSelection block, EntitySelection target,
            EnumHandInteract useType, bool first, ref EnumHandHandling handling) => UseCalls++;
        public override string GetHeldItemName(ItemStack stack) => "guard fixture";
    }
}
