using System;
using System.Collections.Generic;
using System.Linq;
using VRPG.Network;

namespace VRPG.Client.PlayerMenu.Inventory;

public static class InventoryItemGrid
{
    public static PlayerMenuViewState MoveFocus(PlayerMenuViewState state, IReadOnlyList<string> ids,
        PlayerMenuBrowseKey key, int visibleRows, bool grid)
    {
        int columns = grid ? 2 : 1;
        var moved = PlayerMenuNavigation.MoveBrowserFocus(state with { ScrollAnchor = state.ScrollAnchor * columns }, ids, key, visibleRows * columns);
        return moved with { ScrollAnchor = (moved.ScrollAnchor + columns - 1) / columns };
    }
    public static IReadOnlyList<PlayerItemEntryPacket> Visible(IReadOnlyList<PlayerItemEntryPacket> entries,
        int scrollRow, int visibleRows, bool grid) => entries.Skip(Math.Max(0, scrollRow) * (grid ? 2 : 1))
        .Take((Math.Max(0, visibleRows) + 2) * (grid ? 2 : 1)).ToArray();
}
