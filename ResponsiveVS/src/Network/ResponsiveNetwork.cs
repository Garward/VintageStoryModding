using System;
using ResponsiveVS.Config;
using ResponsiveVS.Diagnostics;
using ResponsiveVS.Network.Messages;
using ResponsiveVS.Threading;
using Vintagestory.API.Client;
using Vintagestory.API.Server;

namespace ResponsiveVS.Network;

public sealed class ResponsiveNetwork
{
    private readonly ICoreClientAPI clientApi;
    private readonly ICoreServerAPI serverApi;
    private readonly MainThreadDispatcher dispatcher;
    private IClientNetworkChannel clientChannel;
    private IServerNetworkChannel serverChannel;
    private bool handshakeAccepted;

    public string ClientSessionId { get; private set; }

    public bool IsClient => clientApi != null;
    public bool IsServer => serverApi != null;
    public bool IsChannelConnected => clientChannel?.Connected == true || serverChannel != null;
    public bool IsOwnershipEnabled => handshakeAccepted && IsChannelConnected && ResponsiveVSConfigSystem.Config.Transactions.EnableInventoryOwnership;

    public event Action<IServerPlayer, InventoryTransactionRequest> ServerTransactionRequestReceived;
    public event Action<InventoryTransactionResult> ClientTransactionResultReceived;
    public event Action<IServerPlayer, InventorySnapshotRequest> ServerSnapshotRequestReceived;
    public event Action<InventorySnapshotResult> ClientSnapshotResultReceived;

    private ResponsiveNetwork(ICoreClientAPI clientApi, ICoreServerAPI serverApi, MainThreadDispatcher dispatcher)
    {
        this.clientApi = clientApi;
        this.serverApi = serverApi;
        this.dispatcher = dispatcher;
    }

    public static ResponsiveNetwork RegisterClient(ICoreClientAPI api, MainThreadDispatcher dispatcher)
    {
        ResponsiveNetwork network = new ResponsiveNetwork(api, null, dispatcher);
        network.ClientSessionId = Guid.NewGuid().ToString("N");
        network.clientChannel = api.Network.RegisterChannel(ResponsiveProtocol.ChannelName) as IClientNetworkChannel;
        network.RegisterMessageTypes(network.clientChannel);
        network.clientChannel
            .SetMessageHandler<ResponsiveHandshakeResult>(network.OnClientHandshakeResult)
            .SetMessageHandler<InventoryTransactionResult>(network.OnClientTransactionResult)
            .SetMessageHandler<InventorySnapshotResult>(network.OnClientSnapshotResult)
            .SetMessageHandler<ResponsiveDiagToggle>(network.OnClientDiagToggle);
        return network;
    }

    public static ResponsiveNetwork RegisterServer(ICoreServerAPI api, MainThreadDispatcher dispatcher)
    {
        ResponsiveNetwork network = new ResponsiveNetwork(null, api, dispatcher);
        network.serverChannel = api.Network.RegisterChannel(ResponsiveProtocol.ChannelName);
        network.RegisterMessageTypes(network.serverChannel);
        network.serverChannel
            .SetMessageHandler<ResponsiveHandshakeHello>(network.OnServerHandshakeHello)
            .SetMessageHandler<InventoryTransactionRequest>(network.OnServerTransactionRequest)
            .SetMessageHandler<InventorySnapshotRequest>(network.OnServerSnapshotRequest);
        return network;
    }

    public void TrySendHandshake()
    {
        if (clientChannel?.Connected != true)
        {
            handshakeAccepted = false;
            ResponsiveDiagnostics.Verbose("Handshake skipped; responsivevs channel is not connected.");
            return;
        }

        clientChannel.SendPacket(new ResponsiveHandshakeHello
        {
            ProtocolVersion = ResponsiveProtocol.ProtocolVersion,
            ClientModVersion = ResponsiveProtocol.ModVersion,
            ClientSessionId = ClientSessionId,
            SupportedFeatures = new[] { "diagnostics", "transactions-v1" }
        });
    }

    public bool TrySendTransaction(InventoryTransactionRequest request)
    {
        if (clientChannel?.Connected != true || !handshakeAccepted)
        {
            ResponsiveDiagnostics.Verbose("Transaction not sent because ownership is not handshaken.");
            return false;
        }

        request.ClientSessionId = ClientSessionId;
        clientChannel.SendPacket(request);
        PerfCounters.RecordStarted();
        return true;
    }

    public void SendTransactionResult(IServerPlayer player, InventoryTransactionResult result)
    {
        serverChannel?.SendPacket(result, player);
    }

    public void RequestSnapshot(InventorySnapshotRequest request)
    {
        if (clientChannel?.Connected != true || !handshakeAccepted) return;
        request.ClientSessionId = ClientSessionId;
        clientChannel.SendPacket(request);
    }

    public void SendSnapshotResult(IServerPlayer player, InventorySnapshotResult result)
    {
        serverChannel?.SendPacket(result, player);
    }

    public void DisableOwnership(string reason)
    {
        handshakeAccepted = false;
        ResponsiveDiagnostics.Basic("Ownership disabled: {0}", reason);
    }

    private void OnClientHandshakeResult(ResponsiveHandshakeResult result)
    {
        dispatcher.RunClientMain(() =>
        {
            if (result == null || !result.Accepted || result.ProtocolVersion != ResponsiveProtocol.ProtocolVersion)
            {
                handshakeAccepted = false;
                ResponsiveDiagnostics.Basic("Handshake rejected: {0}", result?.Reason ?? "no result");
                return;
            }

            handshakeAccepted = true;
            ResponsiveDiagnostics.Basic("Handshake accepted by server version {0}", result.ServerModVersion);
        }, "responsivevs-client-handshake");
    }

    private void OnClientTransactionResult(InventoryTransactionResult result)
    {
        dispatcher.RunClientMain(() => ClientTransactionResultReceived?.Invoke(result), "responsivevs-client-transaction-result");
    }

    private void OnClientSnapshotResult(InventorySnapshotResult result)
    {
        dispatcher.RunClientMain(() => ClientSnapshotResultReceived?.Invoke(result), "responsivevs-client-snapshot-result");
    }

    private void OnClientDiagToggle(ResponsiveDiagToggle toggle)
    {
        dispatcher.RunClientMain(() =>
        {
            if (toggle != null)
            {
                ResponsiveDiagnostics.SetLevel((ResponsiveDiagnosticsLevel)toggle.Level);
            }
        }, "responsivevs-client-diag-toggle");
    }

    private void OnServerHandshakeHello(IServerPlayer player, ResponsiveHandshakeHello hello)
    {
        dispatcher.RunServerMain(() =>
        {
            bool accepted = hello != null && hello.ProtocolVersion == ResponsiveProtocol.ProtocolVersion;
            serverChannel.SendPacket(new ResponsiveHandshakeResult
            {
                ProtocolVersion = ResponsiveProtocol.ProtocolVersion,
                ServerModVersion = ResponsiveProtocol.ModVersion,
                Accepted = accepted,
                Reason = accepted ? "ok" : "protocol mismatch",
                EnabledFeatures = accepted ? new[] { "diagnostics", "transactions-v1" } : Array.Empty<string>()
            }, player);

            ResponsiveDiagnostics.Basic("Handshake {0} for {1} clientVersion={2} clientProtocol={3}",
                accepted ? "accepted" : "rejected",
                player?.PlayerName,
                hello?.ClientModVersion,
                hello?.ProtocolVersion ?? -1);
        }, "responsivevs-server-handshake");
    }

    private void OnServerTransactionRequest(IServerPlayer player, InventoryTransactionRequest request)
    {
        dispatcher.RunServerMain(() => ServerTransactionRequestReceived?.Invoke(player, request), "responsivevs-server-transaction-request");
    }

    private void OnServerSnapshotRequest(IServerPlayer player, InventorySnapshotRequest request)
    {
        dispatcher.RunServerMain(() => ServerSnapshotRequestReceived?.Invoke(player, request), "responsivevs-server-snapshot-request");
    }

    private void RegisterMessageTypes(IClientNetworkChannel channel)
    {
        channel
            .RegisterMessageType<ResponsiveHandshakeHello>()
            .RegisterMessageType<ResponsiveHandshakeResult>()
            .RegisterMessageType<InventoryTransactionRequest>()
            .RegisterMessageType<InventoryTransactionResult>()
            .RegisterMessageType<InventorySnapshotRequest>()
            .RegisterMessageType<InventorySnapshotResult>()
            .RegisterMessageType<ResponsiveDiagToggle>();
    }

    private void RegisterMessageTypes(IServerNetworkChannel channel)
    {
        channel
            .RegisterMessageType<ResponsiveHandshakeHello>()
            .RegisterMessageType<ResponsiveHandshakeResult>()
            .RegisterMessageType<InventoryTransactionRequest>()
            .RegisterMessageType<InventoryTransactionResult>()
            .RegisterMessageType<InventorySnapshotRequest>()
            .RegisterMessageType<InventorySnapshotResult>()
            .RegisterMessageType<ResponsiveDiagToggle>();
    }
}
