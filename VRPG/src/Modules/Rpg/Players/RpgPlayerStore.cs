using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using VRPG.Data.Definitions;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace VRPG.Modules.Rpg.Players;

public sealed class RpgPlayerStore
{
    public void Clear()
    {
        states.Clear();
        storage = null;
        api = null;
    }
    private readonly JsonSerializerSettings serializerSettings = new JsonSerializerSettings
    {
        Formatting = Formatting.None,
        MaxDepth = 64,
        MissingMemberHandling = MissingMemberHandling.Ignore,
        TypeNameHandling = TypeNameHandling.None
    };
    private readonly Dictionary<string, RpgPlayerState> states =
        new Dictionary<string, RpgPlayerState>(StringComparer.OrdinalIgnoreCase);
    private ICoreServerAPI? api;
    private IRpgPlayerStateStorage? storage;

    public RpgPlayerState GetOrCreate(IServerPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return GetOrCreate(player.PlayerUID);
    }

    public RpgPlayerState GetOrCreate(string playerUid)
    {
        if (string.IsNullOrWhiteSpace(playerUid))
        {
            throw new ArgumentException("Player UID is required.", nameof(playerUid));
        }

        string normalizedUid = playerUid.Trim();
        if (states.TryGetValue(normalizedUid, out RpgPlayerState? state))
        {
            state.Normalize();
            return state;
        }

        state = new RpgPlayerState();
        state.Normalize();
        states[normalizedUid] = state;
        Save();
        return state;
    }

    public void Load(ICoreServerAPI api)
    {
        this.api = api;
        storage = new SaveGameRpgPlayerStateStorage(api.WorldManager.SaveGame);
        Load(storage);
    }

    public void Load(IRpgPlayerStateStorage storage)
    {
        this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
        states.Clear();

        try
        {
            byte[]? payload = storage.Read();
            Dictionary<string, RpgPlayerState>? loaded = payload == null || payload.Length == 0
                ? null
                : JsonConvert.DeserializeObject<Dictionary<string, RpgPlayerState>>(
                    Encoding.UTF8.GetString(payload),
                    serializerSettings);
            if (loaded != null)
            {
                foreach (var entry in loaded)
                {
                    if (string.IsNullOrWhiteSpace(entry.Key))
                    {
                        continue;
                    }

                    RpgPlayerState state = entry.Value ?? new RpgPlayerState();
                    state.Normalize();
                    states[entry.Key.Trim()] = state;
                }
            }
        }
        catch (Exception ex)
        {
            api?.Logger.Error(
                "[VRPG/RPG] Failed to load world-owned player RPG data; progression starts unavailable for this server session: {0}",
                ex.Message);
            throw;
        }
    }

    public void Save()
    {
        if (storage == null)
        {
            return;
        }

        string json = JsonConvert.SerializeObject(states, serializerSettings);
        storage.Write(Encoding.UTF8.GetBytes(json));
    }

    public int ReconcileTalents(
        IReadOnlyDictionary<string, int> previousCosts,
        IReadOnlyList<TalentNodeDefinition> activeTalents)
    {
        var nodes = new Dictionary<string, TalentNodeDefinition>(StringComparer.OrdinalIgnoreCase);
        var neighbors = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < activeTalents.Count; i++)
        {
            TalentNodeDefinition node = activeTalents[i];
            string code = NormalizeTalentCode(node.Code);
            nodes[code] = node;
            neighbors.TryAdd(code, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        foreach (TalentNodeDefinition node in activeTalents)
        {
            string code = NormalizeTalentCode(node.Code);
            for (int i = 0; i < node.Links.Length; i++)
            {
                string linked = NormalizeTalentCode(node.Links[i]);
                if (!nodes.ContainsKey(linked))
                {
                    continue;
                }

                neighbors[code].Add(linked);
                neighbors[linked].Add(code);
            }
        }

        int changedPlayers = 0;
        foreach (RpgPlayerState state in states.Values)
        {
            state.Normalize();
            var allocated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < state.Talents.Count; i++)
            {
                allocated.Add(NormalizeTalentCode(state.Talents[i]));
            }

            var retained = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var frontier = new Queue<string>();
            foreach (string code in allocated)
            {
                if (nodes.TryGetValue(code, out TalentNodeDefinition? node) && node.Starter)
                {
                    retained.Add(code);
                    frontier.Enqueue(code);
                }
            }

            while (frontier.Count > 0)
            {
                string current = frontier.Dequeue();
                foreach (string neighbor in neighbors[current])
                {
                    if (allocated.Contains(neighbor) && retained.Add(neighbor))
                    {
                        frontier.Enqueue(neighbor);
                    }
                }
            }

            if (retained.SetEquals(allocated))
            {
                continue;
            }

            int refund = 0;
            foreach (string removed in allocated)
            {
                if (retained.Contains(removed))
                {
                    continue;
                }

                refund += previousCosts.TryGetValue(removed, out int oldCost)
                    ? Math.Max(0, oldCost)
                    : nodes.TryGetValue(removed, out TalentNodeDefinition? current) ? (current.Starter ? 0 : Math.Max(1, current.Cost)) : 1;
            }

            state.Talents.Clear();
            foreach (string code in retained)
            {
                state.Talents.Add(nodes[code].Code);
            }
            state.Talents.Sort(StringComparer.OrdinalIgnoreCase);
            state.UnspentTalentPoints += refund;
            changedPlayers++;
        }

        if (changedPlayers > 0)
        {
            Save();
        }

        return changedPlayers;
    }

    private static string NormalizeTalentCode(string code)
    {
        string value = (code ?? "").Trim();
        return value.Contains(':') ? value : "vrpg:" + value;
    }
}
