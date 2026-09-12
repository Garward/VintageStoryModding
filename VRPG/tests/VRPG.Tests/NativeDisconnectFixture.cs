using System;
using System.Collections.Generic;
using HarmonyLib;
using Vintagestory.Server;

namespace VRPG.Tests;

/// <summary>Runs native reason-packet construction and connection removal; replaces only packet transport.</summary>
internal sealed class NativeDisconnectFixture : IDisposable
{
    private readonly NativeClearCommandFixture native = new();
    private readonly Harmony harmony = new("vrpg.tests.departure." + Guid.NewGuid());
    private static Action<int, Packet_Server>? receive;
    public ServerCoreAPI Api => native.Api;
    public ServerMain Server => native.Server;
    public readonly List<(int ClientId, Packet_Server Packet)> Packets = new();

    public NativeDisconnectFixture(ServerPlayer player)
    {
        var client = new ConnectedClient(17) { Player = player, IsNewClient = true };
        Server.Clients[17] = client;
        Server.totalUpTime = new();
        AccessTools.Field(typeof(ServerPlayer), "client").SetValue(player, client);
        AccessTools.Field(typeof(ServerPlayer), "server").SetValue(player, Server);
        receive = (id, packet) => Packets.Add((id, packet));
        harmony.Patch(AccessTools.Method(typeof(ServerMain), "SendPacket", new[] { typeof(int), typeof(Packet_Server) }),
            prefix: new HarmonyMethod(typeof(NativeDisconnectFixture), nameof(CapturePacket)));
    }

    private static bool CapturePacket(int clientId, Packet_Server packet)
    { receive!(clientId, packet); return false; }

    public void Dispose()
    {
        harmony.UnpatchAll(harmony.Id);
        receive = null;
        native.Dispose();
    }
}
