using System;
using System.Collections.Generic;
using System.Linq;
using VRPG.Inventory;
using VRPG.Network;

namespace VRPG.Client.PlayerMenu.Inventory;

/// <summary>Explicit entry/quantity pairs. Invalidated selections cannot silently become new targets.</summary>
public sealed class InventorySelectionState
{
    private readonly Dictionary<string, long> amounts = new(StringComparer.Ordinal);
    private readonly HashSet<string> unverified = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, long> Amounts => amounts;
    public bool Set(PlayerItemEntryPacket entry, long quantity)
    {
        if (!InventoryItemRow.CanSelectQuantity(entry) || quantity <= 0 || quantity > checked(entry.Quantity - entry.ReservedQuantity)) return false;
        amounts[entry.EntryId] = quantity; unverified.Remove(entry.EntryId); return true;
    }
    public void SelectPage(IEnumerable<PlayerItemEntryPacket> entries)
    { foreach (var entry in entries) Set(entry, checked(entry.Quantity - entry.ReservedQuantity)); }
    public void Reconcile(IEnumerable<PlayerItemEntryPacket> entries)
    {
        var current = entries.ToDictionary(x => x.EntryId, StringComparer.Ordinal);
        foreach (var pair in amounts)
            if (!current.TryGetValue(pair.Key, out var row) || !InventoryItemRow.CanSelectQuantity(row)
                || pair.Value > checked(row.Quantity - row.ReservedQuantity)) unverified.Add(pair.Key);
    }
    public int HiddenCount(IEnumerable<PlayerItemEntryPacket> entries) => amounts.Keys.Except(entries.Select(x => x.EntryId)).Count();
    public bool TryResolve(IEnumerable<PlayerItemEntryPacket> entries, out PlayerItemAmount[] result)
    {
        Reconcile(entries);
        result = unverified.Count == 0 ? amounts.Select(x => new PlayerItemAmount(x.Key, x.Value)).ToArray() : Array.Empty<PlayerItemAmount>();
        return amounts.Count > 0 && unverified.Count == 0;
    }
    public void Clear() { amounts.Clear(); unverified.Clear(); }
}
