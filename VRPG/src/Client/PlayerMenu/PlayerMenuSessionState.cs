using System;
using System.Collections.Generic;
using VRPG.Inventory;

namespace VRPG.Client.PlayerMenu;

/// <summary>Immutable page snapshots owned by the menu session, never by an item container.</summary>
public sealed record PlayerMenuViewState(
    string Category = PlayerItemCategories.Gear, string Search = "", string SelectedEntry = "",
    int ScrollAnchor = 0, string Focus = "", bool EquipmentExpanded = false, bool InspectorOnly = false, int InspectorScroll = 0);

public sealed class PlayerMenuSessionState
{
    private readonly Dictionary<PlayerMenuPage, PlayerMenuViewState> pages = new();
    private PlayerMenuPage libraryReturn = PlayerMenuPage.Inventory;
    private string world = "";
    public bool IsOpen { get; private set; }
    public bool ReducedMotion { get; set; }
    public PlayerMenuPage Page { get; private set; } = PlayerMenuPage.Inventory;
    public PlayerMenuViewState Current => pages.TryGetValue(Page, out var value) ? value : new();
    public void Update(PlayerMenuViewState value) => pages[Page] = value with { ScrollAnchor = Math.Max(0, value.ScrollAnchor) };
    public void Enter(PlayerMenuEntryPoint entry)
    {
        var action = PlayerMenuNavigation.Resolve(entry, IsOpen);
        if (action.ShouldClose) { Close(); return; }
        IsOpen = true; Navigate(action.Page);
        if (action.ExpandEquipment) Update(Current with
        {
            Category = PlayerItemCategories.Gear, EquipmentExpanded = true, InspectorOnly = true,
            SelectedEntry = "", InspectorScroll = 0
        });
    }
    public void Navigate(PlayerMenuPage page)
    {
        Update(Current with { Focus = "" });
        if (page == PlayerMenuPage.Library && Page != page) libraryReturn = Page;
        Page = page;
    }
    public void Back()
    {
        if (Current.Focus.Length > 0) Update(Current with { Focus = "" });
        else if (Current.InspectorOnly) Update(Current with { InspectorOnly = false });
        else if (Current.EquipmentExpanded) Update(Current with { EquipmentExpanded = false });
        else if (Page == PlayerMenuPage.Library) Navigate(libraryReturn);
        else Close();
    }
    public void Close() { Update(Current with { Focus = "" }); IsOpen = false; }
    public void SetWorld(string identity)
    {
        if (world == identity) return;
        world = identity; pages.Clear(); IsOpen = false; Page = PlayerMenuPage.Inventory;
        libraryReturn = PlayerMenuPage.Inventory;
    }
}
