using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.Common;
using Vintagestory.Server;

namespace VRPG.Tests;

// Native registration, offline queue, deserialization and slots; only network broadcast is counted.
internal sealed class NativeClearCommandFixture : IDisposable
{
    private readonly ClassRegistry previousRegistry = ServerMain.ClassRegistry;
    private readonly Vintagestory.Logger previousLogger = ServerMain.Logger;
    private readonly string previousLocale = Lang.CurrentLocale;
    private readonly string previousDefault = Lang.DefaultLocale;
    private readonly string locale = "native-clear-" + Guid.NewGuid();
    public readonly ProbeLogger Logger = (ProbeLogger)RuntimeHelpers.GetUninitializedObject(typeof(ProbeLogger));
    public readonly ServerMain Server = (ServerMain)RuntimeHelpers.GetUninitializedObject(typeof(ServerMain));
    public readonly ServerCoreAPI Api;
    public readonly CmdPlayer Commands;
    public readonly ServerWorldPlayerData Data = ServerWorldPlayerData.CreateNew("target", "target");
    public readonly BroadcastPlayer Target = (BroadcastPlayer)RuntimeHelpers.GetUninitializedObject(typeof(BroadcastPlayer));
    public readonly Item Item = new() { ItemId = 0, Code = new("game:queued-clear-fixture"), MaxStackSize = 64 };
    public Vintagestory.API.Datastructures.OrderedDictionary<string, InventoryBase> Inventories => (Vintagestory.API.Datastructures.OrderedDictionary<string, InventoryBase>)AccessTools.Field(typeof(ServerWorldPlayerData), "inventories").GetValue(Data)!;

    public NativeClearCommandFixture()
    {
        ServerMain.ClassRegistry = new ClassRegistry();
        ServerMain.Logger = Logger;
        Lang.AvailableLanguages[locale] = PlayerDefenseServiceTests.Proxy.Create<ITranslationService>((name, args) => name switch
        {
            "HasTranslation" => true,
            "Get" or "GetUnformatted" or "GetMatching" => args![0],
            _ => null
        });
        Lang.DefaultLocale = locale;
        Lang.ChangeLanguage(locale);
        Server.Config = (ServerConfig)RuntimeHelpers.GetUninitializedObject(typeof(ServerConfig));
        Server.Items = new List<Item> { Item };
        Server.PlayersByUid = new();
        Server.Clients = new();
        Server.ClearPlayerInvs = new(StringComparer.OrdinalIgnoreCase);
        var console = new ServerConsoleClient(-1) { WorldData = ServerWorldPlayerData.CreateNew("console", "console") };
        AccessTools.Field(typeof(ServerMain), "ServerConsoleClient").SetValue(Server, console);
        Api = (ServerCoreAPI)RuntimeHelpers.GetUninitializedObject(typeof(ServerCoreAPI));
        AccessTools.Field(typeof(ServerCoreAPI), "server").SetValue(Api, Server);
        AccessTools.Field(typeof(ServerCoreAPI), "classregistryapi").SetValue(Api, new ClassRegistryAPI(Server, ServerMain.ClassRegistry));
        AccessTools.Field(typeof(ServerCoreAPI), "commandapi").SetValue(Api, new ChatCommandApi(Api));
        AccessTools.Field(typeof(ServerMain), "api").SetValue(Server, Api);
        AccessTools.Field(typeof(ServerPlayer), "worlddata").SetValue(Target, Data);
        var manager = new ServerPlayerInventoryManager(Inventories, Target, Server);
        AccessTools.Field(typeof(ServerPlayer), "inventoryMgr").SetValue(Target, manager);
        Commands = new CmdPlayer(Server);
    }

    public InventoryPlayerHotbar Online()
    {
        Server.PlayersByUid["target"] = Target;
        Server.Clients[1] = new ConnectedClient(1) { WorldData = Data, Player = Target };
        var inv = new InventoryPlayerHotbar("hotbar-target", Api);
        Inventories.Add(inv.InventoryID, inv);
        inv[0]!.Itemstack = new ItemStack(Item, 7);
        return inv;
    }

    public void Serialize(string inventoryUid = "target")
    {
        var inv = new InventoryPlayerHotbar("hotbar-" + inventoryUid, Api);
        inv[0]!.Itemstack = new ItemStack(Item, 7);
        var tree = new TreeAttribute();
        inv.ToTreeAttributes(tree);
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        tree.ToBytes(writer);
        AccessTools.Field(typeof(ServerWorldPlayerData), "inventoriesSerialized").SetValue(Data,
            new Dictionary<string, byte[]> { [inv.InventoryID] = memory.ToArray() });
    }

    public TextCommandResult Wipe(string uid = "target", IPlayer? caller = null) =>
        (TextCommandResult)AccessTools.Method(typeof(CmdPlayer), "WipePlayerInventory").Invoke(Commands,
            new object[] { new PlayerUidName(uid, uid), Args(caller) })!;

    public TextCommandResult Clear(IPlayer? caller)
    {
        var command = Api.ChatCommands.Get("clearinv");
        var handler = (OnCommandDelegate)AccessTools.Field(typeof(ChatCommandImpl), "handler").GetValue(command)!;
        return handler(Args(caller));
    }

    public TextCommandResult RegisteredWipe(string[] uids, IPlayer? caller = null)
    {
        var command = Api.ChatCommands.Get("player")["clearinv"];
        var handler = (OnCommandDelegate)AccessTools.Field(typeof(ChatCommandImpl), "handler").GetValue(command)!;
        var args = Args(caller);
        args.Parsers.Add(PlayerDefenseServiceTests.Proxy.Create<ICommandArgumentParser>((name, _) =>
            name == "GetValue" ? Array.ConvertAll(uids, uid => new PlayerUidName(uid, uid)) : null));
        return handler(args);
    }

    public static TextCommandCallingArgs Args(IPlayer? player)
    {
        var caller = new Caller();
        if (player != null) caller.Player = player;
        return new TextCommandCallingArgs { Caller = caller };
    }

    public void Dispose()
    {
        ServerMain.ClassRegistry = previousRegistry;
        ServerMain.Logger = previousLogger;
        Lang.AvailableLanguages.Remove(locale);
        Lang.DefaultLocale = previousDefault;
        Lang.ChangeLanguage(previousLocale);
    }

    internal sealed class ProbeLogger : NullLogger
    {
        public int Errors;
        protected override void LogImpl(EnumLogType logType, string message, params object[] args)
        { if (logType == EnumLogType.Error) Errors++; }
    }

    internal sealed class BroadcastPlayer : ServerPlayer
    {
        public int Broadcasts;
        public Action? BeforeBroadcast;
        public BroadcastPlayer() : base(null!, null!) { }
        public override void BroadcastPlayerData(bool sendInventory = false) { BeforeBroadcast?.Invoke(); Broadcasts++; }
    }
}
