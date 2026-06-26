using Vintagestory.GameContent;

namespace VintageKinematics.Gui
{
    /// <summary>
    /// Row contract for <see cref="MachineRecipeBrowser{T}"/>. Implement this directly for custom
    /// recipe row rendering, or use <see cref="SimpleRecipeBrowserListItem"/> for ordinary icon,
    /// title, and detail-line rows.
    /// </summary>
    public interface IRecipeBrowserListItem : IFlatListItem
    {
        /// <summary>
        /// Lower scores sort earlier. Return <see cref="int.MaxValue"/> to hide the row for the
        /// current search text.
        /// </summary>
        int SearchScore(string text);

        /// <summary>Human-readable title used as a fallback sort key.</summary>
        string SortTitle { get; }

        /// <summary>Sort key for the active dropdown value, e.g. output, input, or work.</summary>
        string SortKey(string sortMode);

        /// <summary>Stable key used when a machine wants clicking this row to select a recipe.</summary>
        string SelectionKey { get; }

        /// <summary>Short label for selected-recipe buttons and status text.</summary>
        string SelectionLabel { get; }
    }
}
