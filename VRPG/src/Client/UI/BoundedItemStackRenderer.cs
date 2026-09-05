using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VRPG.Client.UI;

/// <summary>Renders item models at Vanilla's proven ratio and clips oversized geometry to its slot.</summary>
public sealed class BoundedItemStackRenderer
{
    private const double DefaultClipInset = 2;
    private readonly ICoreClientAPI api;
    private readonly ElementBounds parentBounds;
    private readonly ElementBounds scissorBounds;

    public BoundedItemStackRenderer(ICoreClientAPI api, ElementBounds parentBounds)
    {
        this.api = api;
        this.parentBounds = parentBounds;
        scissorBounds = ElementBounds.FixedSize(1, 1).WithParent(parentBounds);
    }

    public void Render(
        ItemSlot slot,
        double slotX,
        double slotY,
        double slotSize,
        double coordinateScale,
        double z,
        float deltaTime,
        bool showStackSize)
    {
        if (slot?.Itemstack == null || slotSize <= 0 || coordinateScale <= 0)
        {
            return;
        }

        BoundedItemIconLayout layout = BoundedItemIconLayout.Resolve(slotX, slotY, slotSize);
        double guiScale = Math.Max(0.25, RuntimeEnv.GUIScale);
        double fixedScale = coordinateScale / guiScale;

        scissorBounds.fixedX = layout.ClipX * fixedScale;
        scissorBounds.fixedY = layout.ClipY * fixedScale;
        scissorBounds.fixedWidth = layout.ClipWidth * fixedScale;
        scissorBounds.fixedHeight = layout.ClipHeight * fixedScale;
        scissorBounds.CalcWorldBounds();

        api.Render.PushScissor(scissorBounds, stacking: true);
        try
        {
            api.Render.RenderItemstackToGui(
                slot,
                parentBounds.renderX + layout.CenterX * coordinateScale,
                parentBounds.renderY + layout.CenterY * coordinateScale,
                z,
                (float)(layout.ItemSize * coordinateScale),
                -1,
                deltaTime,
                shading: true,
                rotate: false,
                showStackSize: showStackSize);
        }
        finally
        {
            api.Render.PopScissor();
        }
    }

    public static double ItemToSlotRatio =>
        GuiElementPassiveItemSlot.unscaledItemSize
        / GuiElementPassiveItemSlot.unscaledSlotSize;

    public readonly record struct BoundedItemIconLayout(
        double CenterX,
        double CenterY,
        double ItemSize,
        double ClipX,
        double ClipY,
        double ClipWidth,
        double ClipHeight)
    {
        public static BoundedItemIconLayout Resolve(double slotX, double slotY, double slotSize)
        {
            double inset = Math.Min(DefaultClipInset, Math.Max(0, slotSize / 4));
            double clipSize = Math.Max(0, slotSize - inset * 2);
            return new BoundedItemIconLayout(
                slotX + slotSize / 2,
                slotY + slotSize / 2,
                slotSize * ItemToSlotRatio,
                slotX + inset,
                slotY + inset,
                clipSize,
                clipSize);
        }
    }
}
