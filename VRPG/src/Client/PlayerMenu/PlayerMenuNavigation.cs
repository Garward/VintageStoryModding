using System;
using System.Collections.Generic;

namespace VRPG.Client.PlayerMenu;

public enum PlayerMenuBrowseKey { Up, Down, PageUp, PageDown, Home, End }

/// <summary>Resolves input into a deterministic player-menu action.</summary>
public static class PlayerMenuNavigation
{
    public static bool BrowserHasKeyboardFocus(PlayerMenuViewState view, bool alternatePanes) =>
        !(alternatePanes && view.InspectorOnly) && (view.Focus.Length == 0 || view.Focus == "search"
            || view.Focus == "count" || view.Focus.StartsWith("entry:", StringComparison.Ordinal)
            || view.Focus.StartsWith("category", StringComparison.Ordinal));

    /// <summary>Move focus through the complete query window, keeping selection separate until activation.</summary>
    public static PlayerMenuViewState MoveBrowserFocus(PlayerMenuViewState view, IReadOnlyList<string> entries,
        PlayerMenuBrowseKey key, int visibleRows)
    {
        if (entries.Count == 0 || visibleRows <= 0) return view;
        int index = -1;
        for (int i = 0; i < entries.Count; i++)
            if (view.Focus == "entry:" + entries[i]) { index = i; break; }
        bool hadEntryFocus = index >= 0;
        if (!hadEntryFocus) index = Math.Clamp(view.ScrollAnchor, 0, entries.Count - 1);
        index = Math.Clamp(key switch
        {
            PlayerMenuBrowseKey.Home => 0,
            PlayerMenuBrowseKey.End => entries.Count - 1,
            PlayerMenuBrowseKey.Up => index - (hadEntryFocus ? 1 : 0),
            PlayerMenuBrowseKey.Down => index + (hadEntryFocus ? 1 : 0),
            PlayerMenuBrowseKey.PageUp => index - visibleRows,
            _ => index + visibleRows
        }, 0, entries.Count - 1);
        int anchor = Math.Clamp(view.ScrollAnchor, 0, Math.Max(0, entries.Count - visibleRows));
        if (index < anchor) anchor = index;
        if (index >= anchor + visibleRows) anchor = index - visibleRows + 1;
        return view with { Focus = "entry:" + entries[index], ScrollAnchor = anchor };
    }

    public static PlayerMenuNavigationAction Resolve(PlayerMenuEntryPoint entryPoint, bool menuIsOpen)
    {
        if (menuIsOpen)
        {
            return PlayerMenuNavigationAction.Close();
        }

        return entryPoint switch
        {
            PlayerMenuEntryPoint.InventoryMenu => PlayerMenuNavigationAction.Open(PlayerMenuPage.Inventory),
            PlayerMenuEntryPoint.CharacterMenu => PlayerMenuNavigationAction.Open(
                PlayerMenuPage.Inventory,
                expandEquipment: true),
            PlayerMenuEntryPoint.LegacyHubMenu => PlayerMenuNavigationAction.Open(PlayerMenuPage.Stats),
            _ => PlayerMenuNavigationAction.Open(PlayerMenuPage.Inventory)
        };
    }
}

/// <summary>A requested change to the unified player menu.</summary>
public readonly record struct PlayerMenuNavigationAction(
    bool ShouldOpen,
    bool ShouldClose,
    PlayerMenuPage Page,
    bool ExpandEquipment)
{
    public static PlayerMenuNavigationAction Open(PlayerMenuPage page, bool expandEquipment = false)
    {
        return new PlayerMenuNavigationAction(
            ShouldOpen: true,
            ShouldClose: false,
            Page: page,
            ExpandEquipment: expandEquipment);
    }

    public static PlayerMenuNavigationAction Close()
    {
        return new PlayerMenuNavigationAction(
            ShouldOpen: false,
            ShouldClose: true,
            Page: PlayerMenuPage.Inventory,
            ExpandEquipment: false);
    }
}
