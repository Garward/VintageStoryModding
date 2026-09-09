using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using VRPG.Inventory;
using VRPG.Patches;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Common;
using Vintagestory.Server;

namespace VRPG.Tests;

internal sealed class NativeMutationScope : IDisposable
{
    public readonly Harmony Harmony = new("vrpg.tests.native-mutation." + Guid.NewGuid());
    public static ILogger Logger => PlayerDefenseServiceTests.Proxy.Create<ILogger>((name, args) => { if (name == "Error") throw new Exception(string.Format((string)args![0]!, (object[])args[1]!)); return null; });
    public NativeMutationScope(string custody)
    {
        NativeInventoryMutationGuardPatch.Patch(Harmony, Logger);
        Resolve(custody);
    }
    public void Resolve(string custody) => NativeInventoryMutationGuardPatch.CustodyResolver =
        custody == "absent" ? null : (System.Func<string, PlayerItemCustodySnapshot>)(uid => custody == "throw" ? throw new Exception("authority unavailable")
            : new(custody is "vanilla" or "unknown" or "blank" ? PlayerItemCustodyOwner.Vanilla
                : custody == "canonical" ? PlayerItemCustodyOwner.Canonical : PlayerItemCustodyOwner.Recovery, false));
    public void Dispose()
    {
        NativeInventoryMutationGuardPatch.Reset();
        Harmony.UnpatchAll(Harmony.Id);
    }
}

internal sealed class NativeMutationFixture
{
    public readonly ServerPlayer Player = (ServerPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ServerPlayer));
    public readonly ServerMain Server = (ServerMain)RuntimeHelpers.GetUninitializedObject(typeof(ServerMain));
    public readonly ServerPlayerInventoryManager Manager;
    public readonly EntityPlayer Entity = new();
    public readonly ICoreAPI Api;
    public readonly IWorldAccessor World;
    public readonly ProbeInventory Inventory;
    public readonly NativeInventoryGuardFixture.ProbeInventory Ground = new("ground-player");
    public readonly ProbeItem Item = new() { ItemId = 1, Code = new("game:mutation-test"), MaxStackSize = 64 };
    public readonly List<ItemStack> Spawns = new();
    public readonly List<Action> Queue = new();
    public int Events;
    public bool OwnerAvailable = true;
    public EnumAppSide Side = EnumAppSide.Server;

    public NativeMutationFixture()
    {
        AccessTools.PropertySetter(typeof(Entity), "Properties").Invoke(Entity, new object[] { Properties() });
        var events = PlayerDefenseServiceTests.Proxy.Create<IEventAPI>((name, args) =>
        {
            if (name == "PushEvent") Events++;
            if (name == "EnqueueMainThreadTask") Queue.Add((Action)args![0]!);
            return null;
        });
        World = PlayerDefenseServiceTests.Proxy.Create<IWorldAccessor>((name, args) => name switch
        {
            "PlayerByUid" => OwnerAvailable && (string)args![0]! == "player" ? Player : null,
            "SpawnItemEntity" => Spawn((ItemStack)args![0]!),
            "get_Side" => Side,
            "get_ElapsedMilliseconds" => 1000L,
            _ => null
        });
        Api = PlayerDefenseServiceTests.Proxy.Create<ICoreAPI>((name, _) => name switch
        {
            "get_World" => World, "get_Event" => events, "get_Side" => Side, _ => null
        });
        Entity.World = World;
        Entity.Api = Api;
        Entity.WatchedAttributes.SetString("playerUID", "player");
        var data = new ServerWorldPlayerData { EntityPlayer = Entity, GameMode = EnumGameMode.Survival };
        AccessTools.Field(typeof(ServerWorldPlayerData), "PlayerUID").SetValue(data, "player");
        AccessTools.Field(typeof(ServerPlayer), "worlddata").SetValue(Player, data);
        Inventory = new ProbeInventory(Api);
        Ground.Api = Api;
        Manager = new(new() { ["character-player"] = Inventory, ["ground-player"] = Ground }, Player, Server);
        AccessTools.Field(typeof(ServerPlayer), "inventoryMgr").SetValue(Player, Manager);
    }
    private Entity Spawn(ItemStack stack)
    {
        Spawns.Add(stack);
        var entity = new EntityItem { World = World, Api = Api };
        AccessTools.PropertySetter(typeof(Entity), "Properties").Invoke(entity, new object[] { Properties() });
        return entity;
    }
    private static EntityProperties Properties() => new()
    {
        Server = new EntityServerProperties(Array.Empty<JsonObject>(), new()) { Attributes = new TreeAttribute() },
        Client = new EntityClientProperties(Array.Empty<JsonObject>(), new())
    };
    internal sealed class ProbeInventory : InventoryBasePlayer
    {
        private readonly ItemSlot[] slots;
        public int ModifiedCalls;
        public int DeathCalls;
        public ProbeInventory(ICoreAPI api) : base("character", "player", null!) { Api = api; slots = GenEmptySlots(4); }
        public override int Count => slots.Length;
        public override ItemSlot this[int i] { get => slots[i]; set => slots[i] = value; }
        public override void FromTreeAttributes(ITreeAttribute tree) { }
        public override void ToTreeAttributes(ITreeAttribute tree) { }
        public override void DidModifyItemSlot(ItemSlot slot, ItemStack extractedStack = null!) => ModifiedCalls++;
        public override void OnOwningEntityDeath(Vec3d pos) { DeathCalls++; this[1].Itemstack = null; }
    }
    internal sealed class ProbeItem : Item
    {
        public int DropCalls;
        public int CancelCalls;
        public int StopCalls;
        public override void OnHeldDropped(IWorldAccessor world, IPlayer player, ItemSlot slot, int quantity, ref EnumHandling handling) => DropCalls++;
        public override bool OnHeldInteractCancel(float seconds, ItemSlot slot, EntityAgent entity, BlockSelection block, EntitySelection target, EnumItemUseCancelReason reason)
        { CancelCalls++; return true; }
        public override void OnHeldInteractStop(float seconds, ItemSlot slot, EntityAgent entity, BlockSelection block, EntitySelection target) => StopCalls++;
        public override TransitionState[] UpdateAndGetTransitionStates(IWorldAccessor world, ItemSlot slot) => Array.Empty<TransitionState>();
        public override string GetHeldItemName(ItemStack stack) => "fixture";
    }
}
