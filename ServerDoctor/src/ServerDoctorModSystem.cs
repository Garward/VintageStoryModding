using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.Server;

namespace ServerDoctor;

public class ServerDoctorModSystem : ModSystem
{
    private const string PacketHarmonyId = "garward.serverdoctor.packet";
    private const string TickHarmonyId = "garward.serverdoctor.tick";
    private const int DumpEverySeconds = 5;
    private const int TopN = 20;

    private Harmony packetHarmony;
    private Harmony tickHarmony;
    private ICoreServerAPI sapi;
    private IServerNetworkChannel overlayChannel;
    private bool packetPatchesApplied;
    private bool tickPatchesApplied;
    private bool tickPatchRemovalPending;
    private bool packetDumpListenerRegistered;
    private bool profilerListenerRegistered;
    private long packetDumpListenerId;
    private long profilerListenerId;

    internal static PacketCounter Counter;
    internal static TickProfiler TickProfiler;
    internal static ICoreServerAPI Api;
    internal static volatile bool Enabled;
    internal static bool TickProfilerEnabled => TickProfiler?.Enabled == true;

    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;
        Api = api;
        Counter = new PacketCounter();
        overlayChannel = api.Network.RegisterChannel("serverdoctor")
            .RegisterMessageType<ServerDoctorOverlayPacket>()
            .RegisterMessageType<ServerDoctorControlPacket>()
            .RegisterMessageType<ServerDoctorControlResponsePacket>()
            .SetMessageHandler<ServerDoctorControlPacket>(OnControlPacket);

        TickProfiler = new TickProfiler(api, BroadcastOverlayPacket);
        Enabled = false;

        api.Logger.Notification("[ServerDoctor] Loaded idle. Packet sampling: /serverdoctor on. Tick profiling: /serverdoctor tick on.");

        RegisterCommands(api);
    }

    private void RegisterCommands(ICoreServerAPI api)
    {
        api.ChatCommands
            .Create("serverdoctor")
            .WithDescription("ServerDoctor server diagnostics")
            .RequiresPrivilege(Privilege.controlserver)
            .BeginSubCommand("on")
                .WithDescription("Start sampling outbound packets and dumping every 5s")
                .HandleWith(_ => {
                    if (!EnsurePacketSamplingReady()) return TextCommandResult.Error("ServerDoctor: failed to enable packet sampling patches. Check server log.");
                    Counter?.SnapshotAndReset();
                    Enabled = true;
                    api.Logger.Notification("[ServerDoctor] Sampling ENABLED.");
                    return TextCommandResult.Success("ServerDoctor: enabled.");
                })
            .EndSubCommand()
            .BeginSubCommand("off")
                .WithDescription("Stop sampling")
                .HandleWith(_ => {
                    StopPacketSampling();
                    api.Logger.Notification("[ServerDoctor] Sampling DISABLED.");
                    return TextCommandResult.Success("ServerDoctor: disabled.");
                })
            .EndSubCommand()
            .BeginSubCommand("status")
                .WithDescription("Report current state")
                .HandleWith(_ =>
                    TextCommandResult.Success("ServerDoctor: " + (Enabled ? "ENABLED" : "disabled")))
            .EndSubCommand()
            .BeginSubCommand("dump")
                .WithDescription("Force an immediate dump of the current window")
                .HandleWith(_ => {
                    DumpAndReset();
                    return TextCommandResult.Success("ServerDoctor: dumped current window.");
                })
            .EndSubCommand()
            .BeginSubCommand("tick")
                .WithDescription("Profile server tick usage")
                .BeginSubCommand("on")
                    .WithDescription("Start server tick profiling")
                    .HandleWith(_ => {
                        if (!EnsureTickProfilingReady()) return TextCommandResult.Error("ServerDoctor tick profiler: failed to apply profiler patches. Check server log.");
                        TickProfiler?.Start();
                        api.Logger.Notification("[ServerDoctor/Tick] Profiling ENABLED.");
                        return TextCommandResult.Success("ServerDoctor tick profiler: enabled. Reports log every 10s; use /serverdoctor tick dump or /serverdoctor tick off.");
                    })
                .EndSubCommand()
                .BeginSubCommand("off")
                    .WithDescription("Stop server tick profiling and print a final report")
                    .HandleWith(_ => {
                        string report = StopTickProfiling();
                        LogMultiline(api, report);
                        return TextCommandResult.Success(report);
                    })
                .EndSubCommand()
                .BeginSubCommand("status")
                    .WithDescription("Report current tick profiler state")
                    .HandleWith(_ =>
                        TextCommandResult.Success(TickProfiler?.Status() ?? "ServerDoctor tick profiler unavailable."))
                .EndSubCommand()
                .BeginSubCommand("dump")
                    .WithDescription("Dump and reset the current tick profiler window")
                    .HandleWith(_ => {
                        string report = TickProfiler?.DumpAndReset("manual") ?? "ServerDoctor tick profiler unavailable.";
                        LogMultiline(api, report);
                        return TextCommandResult.Success(report);
                    })
                .EndSubCommand()
            .EndSubCommand();
    }

    public override void Dispose()
    {
        try { StopPacketSampling(); } catch { }
        try { UnregisterProfilerListener(); } catch { }
        try { packetHarmony?.UnpatchAll(PacketHarmonyId); } catch { }
        try { tickHarmony?.UnpatchAll(TickHarmonyId); } catch { }
        try { TickProfiler?.Dispose(); } catch { }
        Counter = null;
        TickProfiler = null;
        Api = null;
        sapi = null;
    }

    private void BroadcastOverlayPacket(ServerDoctorOverlayPacket packet)
    {
        try
        {
            IServerPlayer[] players = sapi?.World?.AllOnlinePlayers?
                .OfType<IServerPlayer>()
                .ToArray();
            if (players == null || players.Length == 0 || overlayChannel == null) return;
            overlayChannel.SendPacket(packet, players);
        }
        catch (Exception e)
        {
            sapi?.Logger.Warning("[ServerDoctor] Failed to send overlay snapshot: {0}", e.Message);
        }
    }

    private void OnControlPacket(IServerPlayer player, ServerDoctorControlPacket packet)
    {
        if (player == null) return;

        string action = packet?.Action ?? "";
        bool allowed = player.HasPrivilege(Privilege.controlserver);
        if (!allowed)
        {
            overlayChannel?.SendPacket(new ServerDoctorControlResponsePacket
            {
                Allowed = false,
                Enabled = TickProfilerEnabled,
                OpenDialog = false,
                Message = "You don't have OP."
            }, player);
            return;
        }

        string message = null;
        bool openDialog = action == "open";
            if (action == "toggleprofiling")
            {
                if (TickProfilerEnabled)
                {
                    string report = StopTickProfiling();
                    LogMultiline(sapi, report);
                    message = "ServerDoctor tick profiler: stopped.";
                }
                else
                {
                    if (EnsureTickProfilingReady())
                    {
                        TickProfiler?.Start();
                        message = "ServerDoctor tick profiler: enabled.";
                    }
                    else
                    {
                        message = "ServerDoctor tick profiler: failed to apply patches. Check server log.";
                    }
                }
            }

        overlayChannel?.SendPacket(new ServerDoctorControlResponsePacket
        {
            Allowed = true,
            Enabled = TickProfilerEnabled,
            OpenDialog = openDialog,
            Message = message
        }, player);
    }

    private static void LogMultiline(ICoreServerAPI api, string report)
    {
        if (api == null || string.IsNullOrEmpty(report)) return;

        string[] lines = report.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < lines.Length; i++)
        {
            api.Logger.Notification(lines[i]);
        }
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
                sapi.Logger.Notification("[ServerDoctor] heartbeat: 0 packets in {0}s window", DumpEverySeconds);
                return;
            }

            var nameByUid = sapi.World.AllOnlinePlayers.ToDictionary(
                p => p.PlayerUID, p => p.PlayerName);

            var top = snap
                .OrderByDescending(kv => kv.Value.Bytes)
                .Take(TopN)
                .ToList();

            sapi.Logger.Notification("[ServerDoctor] === {0:F2} MiB total in {1}s window ===",
                total / 1048576.0, DumpEverySeconds);

            foreach (var kv in top)
            {
                var (uid, kind, source) = kv.Key;
                string name = nameByUid.TryGetValue(uid, out var n) ? n : uid;
                double mibs = kv.Value.Bytes / 1048576.0 / DumpEverySeconds;
                sapi.Logger.Notification("[ServerDoctor]  {0,7:F3} MiB/s -> {1,-16} {2,-22} @ {3}  count={4}",
                    mibs, name, kind, source, kv.Value.Count);
            }
        }
        catch (Exception e)
        {
            sapi.Logger.Error("[ServerDoctor] Dump failed: {0}", e);
        }
    }

    private bool EnsurePacketSamplingReady()
    {
        if (!packetPatchesApplied)
        {
            packetHarmony = new Harmony(PacketHarmonyId);
            try
            {
                PatchClass(packetHarmony, typeof(Patch_ServerMain_SendPacket_IntPacket));
                PatchClass(packetHarmony, typeof(Patch_DummyNetConnection_SendServerPacketDirectly));
                PatchClass(packetHarmony, typeof(Patch_ServerMain_BroadcastArbitraryPacket));
                packetPatchesApplied = true;
            }
            catch (Exception e)
            {
                sapi.Logger.Error("[ServerDoctor] Failed to apply packet sampling patches: {0}", e);
                return false;
            }
        }

        if (!packetDumpListenerRegistered)
        {
            packetDumpListenerId = sapi.Event.RegisterGameTickListener(_ => { if (Enabled) DumpAndReset(); }, DumpEverySeconds * 1000);
            packetDumpListenerRegistered = true;
        }

        return true;
    }

    private void StopPacketSampling()
    {
        Enabled = false;
        Counter?.SnapshotAndReset();

        if (packetDumpListenerRegistered)
        {
            sapi?.Event.UnregisterGameTickListener(packetDumpListenerId);
            packetDumpListenerRegistered = false;
        }

        if (packetPatchesApplied)
        {
            try { packetHarmony?.UnpatchAll(PacketHarmonyId); } catch { }
            packetPatchesApplied = false;
            packetHarmony = null;
        }
    }

    private bool EnsureTickProfilingReady()
    {
        tickPatchRemovalPending = false;

        if (!tickPatchesApplied)
        {
            tickHarmony = new Harmony(TickHarmonyId);
            try
            {
                PatchClass(tickHarmony, typeof(Patch_ServerMain_Process));
                PatchClass(tickHarmony, typeof(Patch_GameTickListener_OnTriggered));
                PatchClass(tickHarmony, typeof(Patch_GameTickListenerBlock_OnTriggered));
                PatchClass(tickHarmony, typeof(Patch_GameDatabase_SetChunks));
                PatchClass(tickHarmony, typeof(Patch_GameDatabase_SetMapChunks));
                PatchClass(tickHarmony, typeof(Patch_GameDatabase_SetMapRegions));
                PatchClass(tickHarmony, typeof(Patch_GameDatabase_GetChunk3));
                PatchClass(tickHarmony, typeof(Patch_GameDatabase_GetChunk4));
                PatchClass(tickHarmony, typeof(Patch_GameDatabase_GetMapChunk));
                PatchClass(tickHarmony, typeof(Patch_GameDatabase_GetMapRegion));
                PatchClass(tickHarmony, typeof(Patch_GameDatabase_StoreSaveGame));
                PatchClass(tickHarmony, typeof(Patch_GameDatabase_StoreSaveGameReusableStream));
                tickPatchesApplied = true;
            }
            catch (Exception e)
            {
                sapi.Logger.Error("[ServerDoctor] Failed to apply tick profiler patches: {0}", e);
                return false;
            }
        }

        if (!profilerListenerRegistered)
        {
            profilerListenerId = sapi.Event.RegisterGameTickListener(OnProfilerTick, 1);
            profilerListenerRegistered = true;
        }

        return true;
    }

    private string StopTickProfiling()
    {
        string report = TickProfiler?.Stop() ?? "ServerDoctor tick profiler unavailable.";
        tickPatchRemovalPending = true;
        return report;
    }

    private void OnProfilerTick(float dt)
    {
        if (TickProfilerEnabled)
        {
            TickProfiler.CollectPreviousFrame();
            TickProfiler.MaybeAutoReport();
            return;
        }

        if (tickPatchRemovalPending && TickProfiler != null && TickProfiler.FrameProfilerStateSettled)
        {
            tickPatchRemovalPending = false;
            UnregisterProfilerListener();
            RemoveTickPatches();
        }
    }

    private void UnregisterProfilerListener()
    {
        if (!profilerListenerRegistered) return;

        sapi?.Event.UnregisterGameTickListener(profilerListenerId);
        profilerListenerRegistered = false;
    }

    private void RemoveTickPatches()
    {
        if (!tickPatchesApplied) return;

        try { tickHarmony?.UnpatchAll(TickHarmonyId); } catch { }
        tickPatchesApplied = false;
        tickHarmony = null;
    }

    private static void PatchClass(Harmony targetHarmony, Type patchType)
    {
        targetHarmony.CreateClassProcessor(patchType).Patch();
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
            if (!ServerDoctorModSystem.Enabled || ServerDoctorModSystem.Counter == null || packet == null) return;

            string uid = ResolveUid(__instance, clientId);
            var (kind, source, size) = PacketInspector.Inspect(packet);
            ServerDoctorModSystem.Counter.Record(uid, kind, source, size);
        }
        catch (Exception e)
        {
            ServerDoctorModSystem.Counter?.Record("?", "<patch-err>", e.GetType().Name, 0);
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
            if (!ServerDoctorModSystem.Enabled || ServerDoctorModSystem.Counter == null || packet == null || !__result) return;

            var (kind, source, size) = PacketInspector.Inspect(packet);
            string uid = "singleplayer";
            try
            {
                var ps = ServerDoctorModSystem.Api?.World?.AllOnlinePlayers;
                if (ps != null && ps.Length > 0) uid = ps[0].PlayerUID;
            }
            catch { }
            ServerDoctorModSystem.Counter.Record(uid, kind, source, size);
        }
        catch (Exception e)
        {
            ServerDoctorModSystem.Counter?.Record("?", "<sp-err>", e.GetType().Name, 0);
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
            if (!ServerDoctorModSystem.Enabled || ServerDoctorModSystem.Counter == null || packet == null) return;

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
                ServerDoctorModSystem.Counter.Record(uid, kind, source, size);
            }
        }
        catch (Exception e)
        {
            ServerDoctorModSystem.Counter?.Record("?", "<bcast-err>", e.GetType().Name, 0);
        }
    }
}
