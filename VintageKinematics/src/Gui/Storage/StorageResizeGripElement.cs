using Cairo;
using Vintagestory.API.Client;

namespace VintageKinematics.Gui.Storage
{
    /// <summary>Visual bottom-edge affordance; the owning dialog handles drag capture.</summary>
    internal sealed class StorageResizeGripElement : GuiElement
    {
        public StorageResizeGripElement(ICoreClientAPI capi, ElementBounds bounds)
            : base(capi, bounds)
        {
            MouseOverCursor = "n-resize";
        }

        public override void ComposeElements(Context context, ImageSurface surface)
        {
            Bounds.CalcWorldBounds();
            double center = Bounds.drawX + Bounds.OuterWidth / 2;
            double y = Bounds.drawY + scaled(4);
            context.LineWidth = scaled(1);
            for (int line = 0; line < 3; line++)
            {
                double halfWidth = scaled(18 - line * 5);
                context.SetSourceRGBA(0.72, 0.59, 0.38, 0.72 - line * 0.12);
                context.MoveTo(center - halfWidth, y + scaled(line * 3));
                context.LineTo(center + halfWidth, y + scaled(line * 3));
                context.Stroke();
            }
        }
    }
}
