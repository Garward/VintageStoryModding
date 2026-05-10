using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.Server;

namespace NetSpy;

public class NetSpyModSystem : ModSystem
{
    private const string HarmonyId = "garward.netspy";
    private const int DumpEverySeconds = 5;
    private const int TopN = 20;

    private Harmony harmony;
    private ICoreServerAPI sapi;

    internal static PacketCounter Counter;
    internal static ICoreServerAPI Api;
    internal static volatile bool Enabled;

    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;
        Api = api;
        Counter = new PacketCounter();
        Enabled = false;

        harmony = new Harmony(HarmonyId);
        try
        {
            harmony.PatchAll(typeof(NetSpyModSystem).Assembly);
            api.Logger.Notification("[NetSpy] Loaded. Disabled by default — run /netspy on to start sampling.");
        }
        catch (Exception e)
        {
            api.Logger.Error("[NetSpy] Failed to apply Harmony patches: {0}", e);
        }

        api.Event.RegisterGameTickListener(_ => { if (Enabled) DumpAndReset(); }, DumpEverySeconds * 1000);

        RegisterCommands(api);
    }

    private void RegisterCommands(ICoreServerAPI api)
    {
        api.ChatCommands
            .Create("netspy")
            .WithDescription("NetSpy diagnostic packet sampler")
            .RequiresPrivilege(Privilege.controlserver)
            .BeginSubCommand("on")
                .WithDescription("Start sampling outbound packets and dumping every 5s")
                .HandleWith(_ => {
                    Counter?.SnapshotAndReset();
                    Enabled = true;
                    api.Logger.Notification("[NetSpy] Sampling ENABLED.");
                    return TextCommandResult.Success("NetSpy: enabled.");
                })
            .EndSubCommand()
            .BeginSubCommand("off")
                .WithDescription("Stop sampling")
                .HandleWith(_ => {
                    Enabled = false;
                    Counter?.SnapshotAndReset();
                    api.Logger.Notification("[NetSpy] Sampling DISABLED.");
                    return TextCommandResult.Success("NetSpy: disabled.");
                })
            .EndSubCommand()
            .BeginSubCommand("status")
                .WithDescription("Report current state")
                .HandleWith(_ =>
                    TextCommandResult.Success("NetSpy: " + (Enabled ? "ENABLED" : "disabled")))
            .EndSubCommand()
            .BeginSubCommand("dump")
                .WithDescription("Force an immediate dump of the current window")
                .HandleWith(_ => {
                    DumpAndReset();
                    return TextCommandResult.Success("NetSpy: dumped current window.");
                })
            .EndSubCommand();
    }

    public override void Dispose()
    {
        try { harmony?.UnpatchAll(HarmonyId); } catch { }
        Counter = null;
        Api = null;
        sapi = null;
    }

    private void DumpAndReset()
    {
        try
        {
            var snap = Counter?.SnapshotAndReset();
            long total = 0;
            int entries = 0;
            if (snap != null)
            {
                foreach (var kv in snap) { total += kv.Value.Bytes; entries++; }
            }

            if (entries == 0)
            {
                sapi.Logger.Notification("[NetSpy] heartbeat: 0 packets in {0}s window", DumpEverySeconds);
                return;
            }

            var nameByUid = sapi.World.AllOnlinePlayers.ToDictionary(
                p => p.PlayerUID, p => p.PlayerName);

            var top = snap
                .OrderByDescending(kv => kv.Value.Bytes)
                .Take(TopN)
                .ToList();

            sapi.Logger.Notification("[NetSpy] === {0:F2} MiB total in {1}s window ===",
                total / 1048576.0, DumpEverySeconds);

            foreach (var kv in top)
            {
                var (uid, kind, source) = kv.Key;
                string name = nameByUid.TryGetValue(uid, out var n) ? n : uid;
                double mibs = kv.Value.Bytes / 1048576.0 / DumpEverySeconds;
                sapi.Logger.Notification("[NetSpy]  {0,7:F3} MiB/s -> {1,-16} {2,-22} @ {3}  count={4}",
                    mibs, name, kind, source, kv.Value.Count);
            }
        }
        catch (Exception e)
        {
            sapi.Logger.Error("[NetSpy] Dump failed: {0}", e);
        }
    }
}

[HarmonyPatch(typeof(ServerMain), nameof(ServerMain.SendPacket),
    new[] { typeof(int), typeof(Packet_Server) })]
internal static class Patch_ServerMain_SendPacket_IntPacket
{
    [HarmonyPostfix]
    public static void Postfix(ServerMain __instance, int clientId, Packet_Server packet)
    {
        try
        {
            if (!NetSpyModSystem.Enabled || NetSpyModSystem.Counter == null || packet == null) return;

            string uid = ResolveUid(__instance, clientId);
            var (kind, source, size) = PacketInspector.Inspect(packet);
            NetSpyModSystem.Counter.Record(uid, kind, source, size);
        }
        catch (Exception e)
        {
            NetSpyModSystem.Counter?.Record("?", "<patch-err>", e.GetType().Name, 0);
        }
    }

    private static string ResolveUid(ServerMain server, int clientId)
    {
        try
        {
            if (server.Clients.TryGetValue(clientId, out var c))
            {
                var uid = c.Player?.PlayerUID;
                if (!string.IsNullOrEmpty(uid)) return uid;
            }
        }
        catch { }
        return "client:" + clientId;
    }
}

[HarmonyPatch(typeof(DummyNetConnection), "SendServerPacketDirectly",
    new[] { typeof(Packet_Server) })]
internal static class Patch_DummyNetConnection_SendServerPacketDirectly
{
    [HarmonyPostfix]
    public static void Postfix(Packet_Server packet, bool __result)
    {
        try
        {
            if (!NetSpyModSystem.Enabled || NetSpyModSystem.Counter == null || packet == null || !__result) return;

            var (kind, source, size) = PacketInspector.Inspect(packet);
            string uid = "singleplayer";
            try
            {
                var ps = NetSpyModSystem.Api?.World?.AllOnlinePlayers;
                if (ps != null && ps.Length > 0) uid = ps[0].PlayerUID;
            }
            catch { }
            NetSpyModSystem.Counter.Record(uid, kind, source, size);
        }
        catch (Exception e)
        {
            NetSpyModSystem.Counter?.Record("?", "<sp-err>", e.GetType().Name, 0);
        }
    }
}

[HarmonyPatch(typeof(ServerMain), "BroadcastArbitraryPacket",
    new[] { typeof(Packet_Server), typeof(IServerPlayer[]) })]
internal static class Patch_ServerMain_BroadcastArbitraryPacket
{
    [HarmonyPostfix]
    public static void Postfix(ServerMain __instance, Packet_Server packet, IServerPlayer[] skipPlayers)
    {
        try
        {
            if (!NetSpyModSystem.Enabled || NetSpyModSystem.Counter == null || packet == null) return;

            var (kind, source, size) = PacketInspector.Inspect(packet);

            var skipIds = skipPlayers == null
                ? new HashSet<int>()
                : new HashSet<int>(skipPlayers.Where(p => p != null).Select(p => p.ClientId));

            foreach (var c in __instance.Clients.Values)
            {
                if (c == null) continue;
                if (skipIds.Contains(c.Id)) continue;
                var st = c.State;
                if (st == EnumClientState.Offline || st == EnumClientState.Queued) continue;

                string uid = c.Player?.PlayerUID ?? ("client:" + c.Id);
                NetSpyModSystem.Counter.Record(uid, kind, source, size);
            }
        }
        catch (Exception e)
        {
            NetSpyModSystem.Counter?.Record("?", "<bcast-err>", e.GetType().Name, 0);
        }
    }
}
