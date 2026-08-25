using System.Collections.Generic;
using Vintagestory.API.Client;

namespace VintageKinematics.Gui.Storage
{
    internal sealed class StorageTerminalLayout
    {
        public const double Width = 620.0;

        public ElementBounds Status { get; }
        public ElementBounds Capacity { get; }
        public ElementBounds Search { get; }
        public ElementBounds Sort { get; }
        public ElementBounds GridInset { get; }
        public ElementBounds Grid { get; }
        public ElementBounds Previous { get; }
        public ElementBounds PageText { get; }
        public ElementBounds Next { get; }
        public ElementBounds Footer { get; }
        public ElementBounds ResizeGrip { get; }
        public ElementBounds Background { get; }

        public int Rows { get; private set; }

        public StorageTerminalLayout(int rows)
        {
            Status = ElementBounds.Fixed(8, 18, 300, 22);
            Capacity = ElementBounds.Fixed(320, 18, 292, 22);
            Search = ElementBounds.Fixed(8, 52, 392, 30);
            Sort = ElementBounds.Fixed(410, 52, 202, 30);

            double gridWidth = StorageTerminalTheme.Columns
                * (StorageTerminalTheme.TileSize + StorageTerminalTheme.TileGap)
                - StorageTerminalTheme.TileGap;
            double gridHeight = GridHeight(rows);
            Grid = ElementBounds.Fixed(52, 101, gridWidth, gridHeight);
            GridInset = Grid.FlatCopy().FixedGrow(8);

            Previous = ElementBounds.Fixed(174, 0, 82, 30);
            PageText = ElementBounds.Fixed(266, 0, 88, 22);
            Next = ElementBounds.Fixed(364, 0, 82, 30);
            Footer = ElementBounds.Fixed(52, 0, 560, 48);
            ResizeGrip = ElementBounds.Fixed(0, 0, Width, 15);
            SetRows(rows);

            Background = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            Background.BothSizing = ElementSizing.FitToChildren;
            Background.WithChildren(AllBounds().ToArray());
        }

        private List<ElementBounds> AllBounds()
        {
            return new List<ElementBounds>
            {
                Status, Capacity, Search, Sort, GridInset, Grid,
                Previous, PageText, Next, Footer, ResizeGrip
            };
        }

        public void SetRows(int rows)
        {
            Rows = System.Math.Clamp(
                rows,
                StorageTerminalResizeModel.MinRows,
                StorageTerminalResizeModel.MaxRows);
            double gridHeight = GridHeight(Rows);
            Grid.fixedHeight = gridHeight;
            GridInset.fixedHeight = gridHeight + 16;

            double navigationY = Grid.fixedY + gridHeight + 15;
            Previous.fixedY = navigationY;
            PageText.fixedY = navigationY + 5;
            Next.fixedY = navigationY;
            Footer.fixedY = navigationY + 42;
            ResizeGrip.fixedY = navigationY + 102;
        }

        private static double GridHeight(int rows)
        {
            return System.Math.Max(StorageTerminalResizeModel.MinRows, rows)
                * StorageTerminalTheme.RowStep
                - StorageTerminalTheme.TileGap;
        }
    }
}
