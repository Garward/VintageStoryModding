using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;

namespace VRPG.Inventory;

/// <summary>Provides one independently locked authoritative store per stable player identity.</summary>
public sealed class PlayerItemStoreDirectory
{
    private readonly ConcurrentDictionary<string, IPlayerItemStore> stores =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, IPlayerItemStore> createStore;

    public PlayerItemStoreDirectory(Func<string, IPlayerItemStore>? createStore = null)
    {
        this.createStore = createStore ?? (_ => new InMemoryPlayerItemStore());
    }

    public IPlayerItemStore ForPlayer(string playerUid)
    {
        if (string.IsNullOrWhiteSpace(playerUid))
            throw new ArgumentException("A stable player UID is required.", nameof(playerUid));
        string normalized = playerUid.Trim();
        return stores.GetOrAdd(normalized, createStore);
    }

    public bool Remove(string playerUid, out IPlayerItemStore? store)
    {
        if (string.IsNullOrWhiteSpace(playerUid))
        {
            store = null;
            return false;
        }
        return stores.TryRemove(playerUid.Trim(), out store);
    }

    public void Set(string playerUid, IPlayerItemStore store)
    {
        if (string.IsNullOrWhiteSpace(playerUid))
            throw new ArgumentException("A stable player UID is required.", nameof(playerUid));
        stores[playerUid.Trim()] = store ?? throw new ArgumentNullException(nameof(store));
    }

    public IReadOnlyList<KeyValuePair<string, IPlayerItemStore>> Snapshot() =>
        stores
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public void ReplaceAll(IEnumerable<KeyValuePair<string, IPlayerItemStore>> replacements)
    {
        ArgumentNullException.ThrowIfNull(replacements);
        var staged = new Dictionary<string, IPlayerItemStore>(StringComparer.OrdinalIgnoreCase);
        foreach ((string playerUid, IPlayerItemStore store) in replacements)
        {
            if (string.IsNullOrWhiteSpace(playerUid) || store == null
                || !staged.TryAdd(playerUid.Trim(), store))
            {
                throw new ArgumentException("Replacement stores contain an invalid or duplicate player UID.", nameof(replacements));
            }
        }

        stores.Clear();
        foreach ((string playerUid, IPlayerItemStore store) in staged)
            stores[playerUid] = store;
    }
}
