using System.Linq;
using VRPG.Inventory;

namespace VRPG.Client.PlayerMenu.Components;

public static class TypedTabStrip
{
    public static string[] Categories => PlayerItemCategories.Required.ToArray();
    public static bool Contains(string category) => PlayerItemCategories.Required.Contains(category);
}
