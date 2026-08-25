using Vintagestory.API.Client;

namespace VintageKinematics.Gui.Storage
{
    internal static class StorageTerminalTheme
    {
        public const int Columns = 10;
        public const int Rows = 5;
        public const double TileSize = 50.0;
        public const double TileGap = 3.0;
        public const double RowStep = TileSize + TileGap;
        public static double ItemSize => TileSize
            * GuiElementPassiveItemSlot.unscaledItemSize
            / GuiElementPassiveItemSlot.unscaledSlotSize;

        public static CairoFont MutedText()
        {
            CairoFont font = CairoFont.WhiteSmallText();
            font.Color[3] = 0.72;
            return font;
        }
    }
}
