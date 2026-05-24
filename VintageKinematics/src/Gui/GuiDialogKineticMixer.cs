using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Gui
{
    public class GuiDialogKineticMixer : GuiDialogBlockEntity
    {
        private readonly Func<float> getProgress;
        private readonly Func<float> getProgressMax;
        private readonly Func<bool> getCanProgress;
        private float drawnProgress = -1f;
        private float drawnProgressMax = -1f;
        private bool drawnProgressActive;
        private long lastProgressRedrawMs;
        private const string ProgressBarKey = "mixer-progress";

        public override double DrawOrder => 0.2;

        public GuiDialogKineticMixer(
            string title,
            InventoryBase inventory,
            BlockPos pos,
            Func<float> getProgress,
            Func<float> getProgressMax,
            Func<bool> getCanProgress,
            ICoreClientAPI capi)
            : base(title, inventory, pos, capi)
        {
            this.getProgress = getProgress;
            this.getProgressMax = getProgressMax;
            this.getCanProgress = getCanProgress;
            if (IsDuplicate) return;
            ComposeDialog(title);
        }

        public override void OnRenderGUI(float deltaTime)
        {
            RefreshProgressBar(false);
            base.OnRenderGUI(deltaTime);
        }

        private void ComposeDialog(string title)
        {
            double slotPad = GuiElementItemSlotGridBase.unscaledSlotPadding;
            double slotSize = GuiElementPassiveItemSlot.unscaledSlotSize;
            double rowWidth = System.Math.Max(3 * (slotSize + slotPad), 260.0);

            ElementBounds inputLabelBounds = ElementBounds.Fixed(slotPad, slotPad, rowWidth, 22.0);
            ElementBounds inputSlotsBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, slotPad, slotPad + 24.0, 3, 3);
            ElementBounds progressBounds = ElementBounds.Fixed(slotPad, inputSlotsBounds.fixedY + inputSlotsBounds.fixedHeight + 8.0, rowWidth, 18.0);
            ElementBounds outputLabelBounds = ElementBounds.Fixed(slotPad, progressBounds.fixedY + progressBounds.fixedHeight + 10.0, rowWidth, 22.0);
            ElementBounds outputSlotsBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, slotPad, outputLabelBounds.fixedY + 24.0, 3, 3);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(inputLabelBounds, inputSlotsBounds, progressBounds, outputLabelBounds, outputSlotsBounds);

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.RightMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0.0);

            string inputLabel = Lang.Get("vintagekinematics:kineticmixer-input");
            string outputLabel = Lang.Get("vintagekinematics:kineticmixer-outputs");
            int[] inputSlots = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 };
            int[] outputSlots = new[] { 9, 10, 11, 12, 13, 14, 15, 16, 17 };

            SingleComposer = capi.Gui.CreateCompo("kineticmixer-" + BlockEntityPosition, dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(title, CloseIconPressed)
                .BeginChildElements(bgBounds)
                    .AddStaticText(inputLabel, CairoFont.WhiteSmallText(), inputLabelBounds)
                    .AddItemSlotGrid(Inventory, DoSendPacket, 3, inputSlots, inputSlotsBounds, "inputslots")
                    .AddDynamicCustomDraw(progressBounds, OnDrawProgressBar, ProgressBarKey)
                    .AddStaticText(outputLabel, CairoFont.WhiteSmallText(), outputLabelBounds)
                    .AddItemSlotGrid(Inventory, DoSendPacket, 3, outputSlots, outputSlotsBounds, "outputslots")
                .EndChildElements()
                .Compose();
            RefreshProgressBar(true);
        }

        private void RefreshProgressBar(bool force)
        {
            if (SingleComposer == null) return;
            bool active = getCanProgress?.Invoke() == true;
            float progress = active ? getProgress?.Invoke() ?? 0f : 0f;
            float progressMax = active ? getProgressMax?.Invoke() ?? 1f : 1f;
            if (progressMax <= 0f) progressMax = 1f;

            bool changed = active != drawnProgressActive || Math.Abs(progress - drawnProgress) > 0.001f || Math.Abs(progressMax - drawnProgressMax) > 0.001f;
            if (!force && !changed) return;
            if (!force && capi.ElapsedMilliseconds - lastProgressRedrawMs < 125) return;

            drawnProgressActive = active;
            drawnProgress = progress;
            drawnProgressMax = progressMax;
            SingleComposer.GetCustomDraw(ProgressBarKey)?.Redraw();
            lastProgressRedrawMs = capi.ElapsedMilliseconds;
        }

        private void OnDrawProgressBar(Context ctx, ImageSurface surface, ElementBounds currentBounds)
        {
            float max = drawnProgressMax > 0f ? drawnProgressMax : 1f;
            float frac = GameMath.Clamp(drawnProgress / max, 0f, 1f);
            double width = currentBounds.InnerWidth;
            double height = currentBounds.InnerHeight;
            double radius = GuiElement.scaled(2.0);

            ctx.Save();
            RoundedRectangle(ctx, 0.0, 0.0, width, height, radius);
            ctx.SetSourceRGBA(0.05, 0.04, 0.03, 0.82);
            ctx.FillPreserve();
            ctx.SetSourceRGBA(0.55, 0.46, 0.34, 0.9);
            ctx.LineWidth = GuiElement.scaled(1.0);
            ctx.Stroke();

            if (drawnProgressActive && frac > 0f)
            {
                double inset = GuiElement.scaled(2.0);
                double fillWidth = Math.Max(0.0, (width - 2.0 * inset) * frac);
                RoundedRectangle(ctx, inset, inset, fillWidth, Math.Max(0.0, height - 2.0 * inset), radius);
                using (LinearGradient gradient = new LinearGradient(0, 0, width, 0))
                {
                    gradient.AddColorStop(0, new Color(0.72, 0.36, 0.08, 1));
                    gradient.AddColorStop(1, new Color(0.95, 0.68, 0.18, 1));
                    ctx.SetSource(gradient);
                    ctx.Fill();
                }
            }

            ctx.Restore();
        }

        private static void RoundedRectangle(Context ctx, double x, double y, double width, double height, double radius)
        {
            if (width <= 0.0 || height <= 0.0) return;
            radius = Math.Min(radius, Math.Min(width, height) / 2.0);
            ctx.NewSubPath();
            ctx.Arc(x + width - radius, y + radius, radius, -Math.PI / 2.0, 0.0);
            ctx.Arc(x + width - radius, y + height - radius, radius, 0.0, Math.PI / 2.0);
            ctx.Arc(x + radius, y + height - radius, radius, Math.PI / 2.0, Math.PI);
            ctx.Arc(x + radius, y + radius, radius, Math.PI, 3.0 * Math.PI / 2.0);
            ctx.ClosePath();
        }
    }
}
