using VRPG.Inventory;
using VRPG.Network;

namespace VRPG.Client.PlayerMenu.Inventory;

public sealed record InventoryQueryState(string Category = PlayerItemCategories.Gear, string Search = "",
    PlayerItemLocation Location = PlayerItemLocation.Stored, PlayerItemFilters Filters = PlayerItemFilters.None,
    PlayerItemSortOrder Sort = PlayerItemSortOrder.Name, int Offset = 0, int CategoryOffset = 0)
{
    public PlayerItemQueryRequest ToRequest() => new() { Category = Category, Search = Search, Location = Location,
        Filters = Filters, Sort = Sort, Offset = Offset, Limit = 64, CategoryOffset = CategoryOffset, CategoryLimit = 64 };
}
