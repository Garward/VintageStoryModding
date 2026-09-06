using System;
using System.Collections.Generic;
using System.Linq;
using VRPG.Client.PlayerMenu.Components;
using VRPG.Inventory;

namespace VRPG.Client.PlayerMenu.Shell;

public sealed record GalleryEntry(string Id, string NameKey, string AssetCode, long Quantity);

/// <summary>Deliberately isolated local examples. No item store, channel, mutation, or readiness capability.</summary>
public sealed class PlayerMenuGallery
{
    public MechanicalDisclosure Equipment { get; } = new();
    public MenuControlState State { get; private set; }
    public bool LongLabels { get; private set; }
    public int Count { get; private set; } = 120;
    public string FeedbackKey { get; private set; } = "gallery-isolated";
    public void CycleState() { State = (MenuControlState)(((int)State + 1) % Enum.GetValues<MenuControlState>().Length); FeedbackKey = "gallery-state-" + State.ToString().ToLowerInvariant(); }
    public void CycleCount() { Count = Count switch { 120 => 512, 512 => 0, 0 => 1, _ => 120 }; }
    public void ToggleLabels() => LongLabels = !LongLabels;
    public void Activate()
    {
        if (!new FittedButton("preview", "gallery-action", State).CanActivate) return;
        State = MenuControlState.Success; FeedbackKey = "gallery-success";
    }
    public IReadOnlyList<GalleryEntry> Entries(string category) => Enumerable.Range(0, Math.Min(Count, category == PlayerItemCategories.Gear ? 120 : 512)).Select(index =>
        new GalleryEntry("preview-" + index, LongLabels ? "gallery-item-long" : category == PlayerItemCategories.Materials ? "gallery-material" : "gallery-item",
            category == PlayerItemCategories.Materials ? "game:ingot-copper" : "game:axe-copper",
            category == PlayerItemCategories.Materials ? index == 0 ? long.MaxValue : 12840 + index : 1)).ToArray();
}
