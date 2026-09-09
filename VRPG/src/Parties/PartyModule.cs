using System;
using System.Collections.Generic;
using System.Linq;
using VRPG.Core;
using VRPG.Parties.Networking;
using VRPG.Parties.Persistence;
using Vintagestory.API.Server;

namespace VRPG.Parties;

public sealed class PartyModule : IVrpgModule
{
    private readonly IServerNetworkChannel channel;
    private ICoreServerAPI? api;
    private PartyNetworkService? network;
    private WorldScopedPartyRepository? repository;
    private long tickListener;
    private bool loaded;
    public string Code => "parties";
    public PartyService? Service { get; private set; }

    public PartyModule(IServerNetworkChannel channel) => this.channel = channel;

    public void StartServerSide(ICoreServerAPI api)
    {
        if (this.api != null) throw new InvalidOperationException("Party module is already started.");
        this.api = api;
        network = new(() => Service, OnlinePlayers,
            uid => api.PlayerData.GetPlayerDataByUid(uid)?.LastKnownPlayername);
        // Always register the transport, including before storage initialization or after a refusal.
        network.Attach(channel);
        api.Event.SaveGameLoaded += OnSaveGameLoaded;
        api.Event.PlayerJoin += OnConnect;
        api.Event.PlayerNowPlaying += OnConnect;
        api.Event.PlayerLeave += OnDisconnect;
        api.Event.PlayerDisconnect += OnDisconnect;
        tickListener = api.Event.RegisterGameTickListener(_ => network?.Tick(), 1000);
        foreach (var player in OnlinePlayers()) network.Connect(player);
    }

    private IEnumerable<IServerPlayer> OnlinePlayers() => api?.World?.AllOnlinePlayers?.OfType<IServerPlayer>()
        ?? Enumerable.Empty<IServerPlayer>();

    private void OnSaveGameLoaded()
    {
        if (api == null || loaded) return;
        loaded = true;
        try
        {
            repository = new WorldScopedPartyRepository(api.GetOrCreateDataPath("VRPG"),
                api.WorldManager.SaveGame.SavegameIdentifier);
            Service = new(repository, uid => network?.IsOnline(uid) == true);
            if (Service.Load() != PartyHealth.Healthy)
                api.Logger.Error("[VRPG/Parties] Party storage is unavailable. Existing data was preserved.");
        }
        catch (Exception error)
        {
            api.Logger.Error("[VRPG/Parties] Party storage initialization failed: {0}", error.Message);
            repository?.Dispose();
            repository = null;
            Service = null;
        }
    }

    private void OnConnect(IServerPlayer player) => network?.Connect(player);
    private void OnDisconnect(IServerPlayer player) => network?.Disconnect(player);

    public void Dispose()
    {
        if (api != null)
        {
            api.Event.SaveGameLoaded -= OnSaveGameLoaded;
            api.Event.PlayerJoin -= OnConnect;
            api.Event.PlayerNowPlaying -= OnConnect;
            api.Event.PlayerLeave -= OnDisconnect;
            api.Event.PlayerDisconnect -= OnDisconnect;
            if (tickListener != 0) api.Event.UnregisterGameTickListener(tickListener);
        }
        network?.Dispose();
        network = null;
        repository?.Dispose();
        repository = null;
        Service = null;
        api = null;
        tickListener = 0;
        loaded = false;
    }
}
