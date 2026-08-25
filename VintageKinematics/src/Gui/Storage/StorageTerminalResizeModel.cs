using System;

namespace VintageKinematics.Gui.Storage
{
    /// <summary>Pure row-snapping rules for vertical terminal resizing.</summary>
    internal static class StorageTerminalResizeModel
    {
        public const int MinRows = 5;
        public const int MaxRows = 12;

        public static int RowsForDrag(
            int startRows,
            int mouseDeltaPixels,
            double guiScale,
            int maximumRows)
        {
            double scaledRowHeight = StorageTerminalTheme.RowStep * Math.Max(0.1, guiScale);
            int rowDelta = (int)Math.Round(mouseDeltaPixels / scaledRowHeight);
            return Math.Clamp(
                startRows + rowDelta,
                MinRows,
                Math.Clamp(maximumRows, MinRows, MaxRows));
        }

        public static int MaximumRowsThatFit(
            int currentRows,
            double availablePixelsBelow,
            double guiScale)
        {
            double scaledRowHeight = StorageTerminalTheme.RowStep * Math.Max(0.1, guiScale);
            int additional = (int)Math.Floor(Math.Max(0, availablePixelsBelow) / scaledRowHeight);
            return Math.Clamp(currentRows + additional, MinRows, MaxRows);
        }
    }
}
