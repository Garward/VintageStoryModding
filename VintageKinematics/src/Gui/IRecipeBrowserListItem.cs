using Vintagestory.GameContent;

namespace VintageKinematics.Gui
{
    public interface IRecipeBrowserListItem : IFlatListItem
    {
        int SearchScore(string text);
        string SortTitle { get; }
        string SortKey(string sortMode);
    }
}
