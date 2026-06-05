using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;

namespace VintageKinematics.Gui
{
    public class GuiDialogPrimitiveSieve : GuiDialogBlockEntity, IWorkProgressDialog
    {
        public override double DrawOrder => 0.2;

        private float progress;
        private float progressMax = 1f;
        private long lastRedrawMs;

        public GuiDialogPrimitiveSieve(string title, InventoryBase inventory, BlockPos pos, ICoreClientAPI capi)
            : base(title, inventory, pos, capi)
        {
            if (IsDuplicate) return;
            ComposeDialog(title);
        }

        private void ComposeDialog(string title)
        {
            double slotPad = GuiElementItemSlotGridBase.unscaledSlotPadding;
            double topOffset = 16.0;

            ElementBounds inputLabelBounds = ElementBounds.Fixed(slotPad, slotPad + topOffset, 220.0, 22.0);
            ElementBounds inputSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, slotPad, slotPad + topOffset + 24.0, 1, 1);
            ElementBounds arrowBounds = ElementBounds.Fixed(slotPad + 60.0, slotPad + topOffset + 24.0, 96.0, 48.0);
            ElementBounds outputLabelBounds = ElementBounds.Fixed(slotPad, inputSlotBounds.fixedY + inputSlotBounds.fixedHeight + 8.0, 220.0, 22.0);
            ElementBounds outputSlotsBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, slotPad, outputLabelBounds.fixedY + 24.0, 2, 2);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(inputLabelBounds, inputSlotBounds, arrowBounds, outputLabelBounds, outputSlotsBounds);

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.RightMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0.0);

            string inputLabel = Lang.Get("vintagekinematics:primitivesieve-input");
            string outputLabel = Lang.Get("vintagekinematics:primitivesieve-outputs");
            int[] inputSlot = new[] { 0 };
            int[] outputSlots = new[] { 1, 2, 3, 4 };

            SingleComposer = capi.Gui.CreateCompo("primitivesieve-" + BlockEntityPosition, dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(title, CloseIconPressed)
                .BeginChildElements(bgBounds)
                    .AddStaticText(inputLabel, CairoFont.WhiteSmallText(), inputLabelBounds)
                    .AddItemSlotGrid(Inventory, DoSendPacket, 1, inputSlot, inputSlotBounds, "inputslot")
                    .AddDynamicCustomDraw(arrowBounds, OnDrawProgress, "progress")
                    .AddStaticText(outputLabel, CairoFont.WhiteSmallText(), outputLabelBounds)
                    .AddItemSlotGrid(Inventory, DoSendPacket, 2, outputSlots, outputSlotsBounds, "outputslots")
                .EndChildElements()
                .Compose();

            lastRedrawMs = capi.ElapsedMilliseconds;
        }

        public void Update(float current, float total)
        {
            progress = current;
            progressMax = total > 0f ? total : 1f;
            if (!IsOpened()) return;

            if (capi.ElapsedMilliseconds - lastRedrawMs > 250)
            {
                SingleComposer?.GetCustomDraw("progress")?.Redraw();
                lastRedrawMs = capi.ElapsedMilliseconds;
            }
        }

        private void OnDrawProgress(Context ctx, ImageSurface surface, ElementBounds currentBounds)
        {
            float frac = System.Math.Min(1f, progressMax > 0f ? progress / progressMax : 0f);

            ctx.Save();
            capi.Gui.Icons.DrawArrowRight(ctx, 2);

            ctx.Rectangle(0, 0, GuiElement.scaled(96 * frac), GuiElement.scaled(48));
            ctx.Clip();
            using (LinearGradient gradient = new LinearGradient(0, 0, GuiElement.scaled(96), 0))
            {
                gradient.AddColorStop(0, new Color(0, 0.4, 0, 1));
                gradient.AddColorStop(1, new Color(0.2, 0.6, 0.2, 1));
                ctx.SetSource(gradient);
                capi.Gui.Icons.DrawArrowRight(ctx, 0, false, false);
            }
            ctx.Restore();
        }
    }
}
