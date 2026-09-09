using System.Collections.Generic;
using System.Linq;
using VRPG.Inventory;
using VRPG.Network;

namespace VRPG.Client.PlayerMenu.Inventory;

/// <summary>Bounded presentation snapshot. Unknown/loading totals remain absent until an accepted server page.</summary>
public sealed class InventoryViewModel
{
    public IReadOnlyList<PlayerItemEntryPacket> Entries { get; private set; } = System.Array.Empty<PlayerItemEntryPacket>();
    public IReadOnlyList<PlayerItemCategoryPacket> Categories { get; private set; } = PlayerItemCategories.Required
        .Select(code => new PlayerItemCategoryPacket { Code = code }).ToArray();
    public PlayerItemCapacityPacket? Capacity { get; private set; }
    public int? Total { get; private set; }
    public int RecoveryCount { get; private set; }
    public int CategoryTotal { get; private set; }
    public long StoreRevision { get; private set; }
    public long BuildRevision { get; private set; }
    public int Scroll { get; set; }
    public string SelectedId { get; private set; } = "";
    public PlayerItemEntryPacket? Selected => Entries.FirstOrDefault(x => x.EntryId == SelectedId);
    public PlayerItemDetailResult? Detail { get; internal set; }
    public bool Loading { get; internal set; }
    public bool DetailLoading { get; internal set; }
    public bool Stale { get; internal set; }
    public string Error { get; internal set; } = "";
    public string DetailError { get; internal set; } = "";
    public bool Grid { get; set; }
    public InventorySelectionState Selection { get; } = new();
    public void Begin(bool sameQuery)
    {
        Loading = true; Stale = Capacity != null; Error = ""; Detail = null; DetailLoading = false;
        if (!sameQuery) { Entries = System.Array.Empty<PlayerItemEntryPacket>(); Total = null; SelectedId = ""; Scroll = 0; }
    }
    public void Apply(PlayerItemQueryResult result)
    {
        StoreRevision = result.StoreRevision; BuildRevision = result.BuildRevision;
        Entries = result.Entries; Capacity = result.CapacitySummary; Total = result.Total;
        RecoveryCount = result.RecoveryCount; CategoryTotal = result.CategoryTotal;
        Categories = PlayerItemCategories.Required.Select(code => result.CoreCategories.FirstOrDefault(x => x.Code == code)
            ?? new PlayerItemCategoryPacket { Code = code }).Concat(result.Categories.Where(x => !PlayerItemCategories.Required.Contains(x.Code))).ToArray();
        Loading = Stale = false; Error = "";
        Selection.Reconcile(Entries);
        if (Selected == null) { SelectedId = ""; Detail = null; DetailLoading = false; DetailError = ""; }
    }
    public bool Select(string id)
    {
        if (Stale || Loading || Entries.All(x => x.EntryId != id)) return false;
        SelectedId = id; Detail = null; DetailError = ""; DetailLoading = true; return true;
    }
    public void Reset()
    {
        Entries = System.Array.Empty<PlayerItemEntryPacket>(); Capacity = null; Total = null;
        SelectedId = ""; Detail = null; Selection.Clear(); Loading = DetailLoading = Stale = false;
        Categories = PlayerItemCategories.Required.Select(code => new PlayerItemCategoryPacket { Code = code }).ToArray();
        Scroll = RecoveryCount = CategoryTotal = 0; StoreRevision = BuildRevision = 0; Error = DetailError = "";
    }
}
