using System;
using System.Collections.Generic;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api.Storage;

namespace VintageKinematics.Gui.Storage
{
    /// <summary>Draws snapshot entries without creating a live inventory or slot network.</summary>
    internal sealed class StorageEntryGridElement : GuiElement
    {
        private IReadOnlyList<StoredEntry> entries = Array.Empty<StoredEntry>();
        private readonly List<DummySlot> iconSlots = new();
        private readonly List<LoadedTexture> quantityTextures = new();
        private readonly Action depositCarriedStack;
        private readonly Action<long, bool> withdrawEntry;
        private readonly ItemstackComponentBase tooltipRenderer;
        private readonly ElementBounds iconScissorBounds;
        private int rows;

        public StorageEntryGridElement(
            ICoreClientAPI capi,
            ElementBounds bounds,
            int rows,
            Action depositCarriedStack,
            Action<long, bool> withdrawEntry)
            : base(capi, bounds)
        {
            this.depositCarriedStack = depositCarriedStack;
            this.withdrawEntry = withdrawEntry;
            tooltipRenderer = new ItemstackComponentBase(capi);
            iconScissorBounds = ElementBounds
                .FixedSize(StorageTerminalTheme.TileSize, StorageTerminalTheme.TileSize)
                .WithParent(bounds);
            this.rows = rows;
            MouseOverCursor = "hand";
        }

        public override void ComposeElements(Context context, ImageSurface surface)
        {
            Bounds.CalcWorldBounds();
            double size = scaled(StorageTerminalTheme.TileSize);
            double step = scaled(StorageTerminalTheme.TileSize + StorageTerminalTheme.TileGap);
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < StorageTerminalTheme.Columns; column++)
                {
                    // Cairo composes into the dialog texture, so use dialog-local draw
                    // coordinates here. renderX/renderY are only correct during live GL
                    // rendering and would apply the centered dialog origin a second time.
                    double x = Bounds.drawX + column * step;
                    double y = Bounds.drawY + row * step;
                    context.SetSourceRGBA(0.09, 0.08, 0.065, 0.82);
                    context.Rectangle(x, y, size, size);
                    context.Fill();
                    context.SetSourceRGBA(0.58, 0.46, 0.29, 0.42);
                    context.Rectangle(x + 0.5, y + 0.5, size - 1, size - 1);
                    context.Stroke();
                }
            }
        }

        public void UpdateEntries(IReadOnlyList<StoredEntry> nextEntries)
        {
            DisposeTextures();
            entries = nextEntries ?? Array.Empty<StoredEntry>();
            iconSlots.Clear();

            TextTextureUtil text = new TextTextureUtil(api);
            CairoFont font = CairoFont.WhiteDetailText();
            font.FontWeight = FontWeight.Bold;
            foreach (StoredEntry entry in entries)
            {
                ItemStack stack = entry?.Exemplar;
                if (stack != null) stack.StackSize = 1;
                iconSlots.Add(new DummySlot(stack));
                quantityTextures.Add(text.GenTextTexture(FormatQuantity(entry?.Quantity ?? 0), font, null));
            }
        }

        public void SetRows(int nextRows)
        {
            rows = Math.Clamp(
                nextRows,
                StorageTerminalResizeModel.MinRows,
                StorageTerminalResizeModel.MaxRows);
        }

        public override void RenderInteractiveElements(float deltaTime)
        {
            double size = scaled(StorageTerminalTheme.TileSize);
            double step = scaled(StorageTerminalTheme.TileSize + StorageTerminalTheme.TileGap);
            double itemSize = scaled(StorageTerminalTheme.ItemSize);
            int visibleCount = Math.Min(iconSlots.Count, rows * StorageTerminalTheme.Columns);
            for (int index = 0; index < visibleCount; index++)
            {
                int column = index % StorageTerminalTheme.Columns;
                int row = index / StorageTerminalTheme.Columns;
                double x = Bounds.renderX + column * step;
                double y = Bounds.renderY + row * step;
                DummySlot slot = iconSlots[index];
                if (slot?.Itemstack != null)
                {
                    iconScissorBounds.fixedX = column * StorageTerminalTheme.RowStep;
                    iconScissorBounds.fixedY = row * StorageTerminalTheme.RowStep;
                    iconScissorBounds.CalcWorldBounds();
                    api.Render.PushScissor(iconScissorBounds, stacking: true);
                    api.Render.RenderItemstackToGui(
                        slot,
                        x + size / 2,
                        y + size / 2,
                        120,
                        (float)itemSize,
                        -1,
                        true,
                        false,
                        false);
                    api.Render.PopScissor();
                }

                LoadedTexture quantity = quantityTextures[index];
                api.Render.Render2DTexturePremultipliedAlpha(
                    quantity.TextureId,
                    x + size - quantity.Width - scaled(3),
                    y + size - quantity.Height - scaled(2),
                    quantity.Width,
                    quantity.Height,
                    180,
                    (Vec4f)null);
            }

            int hoveredIndex = EntryIndexAt(api.Input.MouseX, api.Input.MouseY);
            if (hoveredIndex >= 0
                && hoveredIndex < visibleCount
                && iconSlots[hoveredIndex]?.Itemstack != null)
            {
                tooltipRenderer.RenderItemstackTooltip(
                    iconSlots[hoveredIndex],
                    api.Input.MouseX,
                    api.Input.MouseY,
                    deltaTime);
            }
        }

        public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
        {
            base.OnMouseDownOnElement(api, args);
            if (args.Button != EnumMouseButton.Left
                && args.Button != EnumMouseButton.Right) return;

            if (args.Button == EnumMouseButton.Left
                && api.World.Player.InventoryManager.MouseItemSlot?.Empty == false)
            {
                api.Gui.PlaySound("menubutton_press");
                depositCarriedStack?.Invoke();
                args.Handled = true;
                return;
            }

            int index = EntryIndexAt(args.X, args.Y);
            if (index >= 0 && index < entries.Count && entries[index] != null)
            {
                api.Gui.PlaySound("menubutton_press");
                bool shiftPressed = args.Button == EnumMouseButton.Left
                    && (api.Input.KeyboardKeyState[1]
                        || api.Input.KeyboardKeyState[2]);
                withdrawEntry?.Invoke(entries[index].EntryId, shiftPressed);
                args.Handled = true;
            }
        }

        public override void Dispose()
        {
            DisposeTextures();
            tooltipRenderer.Dispose();
            base.Dispose();
        }

        private int EntryIndexAt(double mouseX, double mouseY)
        {
            double size = scaled(StorageTerminalTheme.TileSize);
            double step = scaled(StorageTerminalTheme.TileSize + StorageTerminalTheme.TileGap);
            double localX = mouseX - Bounds.renderX;
            double localY = mouseY - Bounds.renderY;
            if (localX < 0 || localY < 0) return -1;

            int column = (int)(localX / step);
            int row = (int)(localY / step);
            if (column >= StorageTerminalTheme.Columns || row >= rows) return -1;
            if (localX - column * step >= size || localY - row * step >= size) return -1;
            return row * StorageTerminalTheme.Columns + column;
        }

        private void DisposeTextures()
        {
            foreach (LoadedTexture texture in quantityTextures) texture?.Dispose();
            quantityTextures.Clear();
        }

        private static string FormatQuantity(long quantity)
        {
            if (quantity >= 1_000_000_000) return (quantity / 1_000_000_000d).ToString("0.#") + "b";
            if (quantity >= 1_000_000) return (quantity / 1_000_000d).ToString("0.#") + "m";
            if (quantity >= 10_000) return (quantity / 1_000d).ToString("0.#") + "k";
            return quantity.ToString("N0");
        }
    }
}
