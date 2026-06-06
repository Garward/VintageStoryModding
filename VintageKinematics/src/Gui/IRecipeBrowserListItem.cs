using Vintagestory.GameContent;

namespace VintageKinematics.Gui
{
    internal interface IRecipeBrowserListItem : IFlatListItem
    {
        int SearchScore(string text);
        string SortTitle { get; }
    }
}
