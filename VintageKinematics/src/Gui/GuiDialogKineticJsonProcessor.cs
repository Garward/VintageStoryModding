using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Gui
{
    public class GuiDialogKineticJsonProcessor : GuiDialogBlockEntity
    {
        private readonly int inputFirst;
        private readonly int inputLast;
        private readonly int outputFirst;
        private readonly int outputLast;
        private readonly bool showProgressBar;
        private readonly double progressBarWidth;
        private readonly string progressBarAlign;
        private readonly int inputColumnsOverride;
        private readonly int outputColumnsOverride;
        private readonly string inputLabelLangCode;
        private readonly string outputLabelLangCode;
        private readonly string dialogKeyPrefix;
        private readonly MachineProgressBar progressBar;

        public override double DrawOrder => 0.2;

        public GuiDialogKineticJsonProcessor(
            string title,
            InventoryBase inv,
            BlockPos pos,
            int inputFirst,
            int inputLast,
            int outputFirst,
            int outputLast,
            bool showProgressBar,
            double progressBarWidth,
            string progressBarAlign,
            Func<float> getProgress,
            Func<float> getProgressMax,
            Func<bool> getCanProgress,
            ICoreClientAPI capi,
            int inputColumnsOverride = 0,
            int outputColumnsOverride = 0,
            string inputLabelLangCode = null,
            string outputLabelLangCode = null,
            string dialogKeyPrefix = "kineticjsonprocessor")
            : base(title, inv, pos, capi)
        {
            this.inputFirst = inputFirst;
            this.inputLast = inputLast;
            this.outputFirst = outputFirst;
            this.outputLast = outputLast;
            this.showProgressBar = showProgressBar;
            this.progressBarWidth = progressBarWidth;
            this.progressBarAlign = progressBarAlign;
            this.inputColumnsOverride = inputColumnsOverride;
            this.outputColumnsOverride = outputColumnsOverride;
            this.inputLabelLangCode = inputLabelLangCode ?? "vintagekinematics:jsonprocessor-input";
            this.outputLabelLangCode = outputLabelLangCode ?? "vintagekinematics:jsonprocessor-outputs";
            this.dialogKeyPrefix = dialogKeyPrefix ?? "kineticjsonprocessor";
            progressBar = new MachineProgressBar(capi, this.dialogKeyPrefix + "-progress", getProgress, getProgressMax, getCanProgress);
            if (IsDuplicate) return;
            ComposeDialog(title);
        }

        public override void OnRenderGUI(float deltaTime)
        {
            if (showProgressBar) progressBar.Refresh(SingleComposer, false);
            base.OnRenderGUI(deltaTime);
        }

        private void ComposeDialog(string title)
        {
            double slotPad = GuiElementItemSlotGridBase.unscaledSlotPadding;
            double slotSize = GuiElementPassiveItemSlot.unscaledSlotSize;
            int inputCount = inputLast - inputFirst + 1;
            int outputCount = outputLast - outputFirst + 1;
            int inputColumns = inputColumnsOverride > 0 ? Math.Min(inputColumnsOverride, inputCount) : Math.Min(Math.Max(inputCount, 1), 8);
            int outputColumns = outputColumnsOverride > 0 ? Math.Min(outputColumnsOverride, outputCount) : Math.Min(Math.Max(outputCount, 1), 8);
            int inputRows = (inputCount + inputColumns - 1) / inputColumns;
            int outputRows = (outputCount + outputColumns - 1) / outputColumns;
            double rowWidth = Math.Max(Math.Max(inputColumns, outputColumns) * (slotSize + slotPad), 220.0);
            double topOffset = 16.0;

            ElementBounds inputLabelBounds = ElementBounds.Fixed(slotPad, slotPad + topOffset, rowWidth, 22.0);
            ElementBounds inputBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, slotPad, slotPad + topOffset + 24.0, inputColumns, inputRows);
            double barWidth = Math.Min(rowWidth, Math.Max(48.0, progressBarWidth));
            double barX = ProgressBarX(slotPad, rowWidth, barWidth, progressBarAlign);
            ElementBounds progressBounds = ElementBounds.Fixed(barX, inputBounds.fixedY + inputBounds.fixedHeight + 8.0, barWidth, 18.0);
            double outputLabelY = showProgressBar ? progressBounds.fixedY + progressBounds.fixedHeight + 10.0 : inputBounds.fixedY + inputBounds.fixedHeight + 8.0;
            ElementBounds outputLabelBounds = ElementBounds.Fixed(slotPad, outputLabelY, rowWidth, 22.0);
            ElementBounds outputBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, slotPad, outputLabelBounds.fixedY + 24.0, outputColumns, outputRows);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            if (showProgressBar)
            {
                bgBounds.WithChildren(inputLabelBounds, inputBounds, progressBounds, outputLabelBounds, outputBounds);
            }
            else
            {
                bgBounds.WithChildren(inputLabelBounds, inputBounds, outputLabelBounds, outputBounds);
            }

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.RightMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0.0);

            int[] inputSel = SlotRange(inputFirst, inputLast);
            int[] outputSel = SlotRange(outputFirst, outputLast);

            GuiComposer composer = capi.Gui.CreateCompo(dialogKeyPrefix + "-" + BlockEntityPosition, dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(title, CloseIconPressed)
                .BeginChildElements(bgBounds)
                    .AddStaticText(Lang.Get(inputLabelLangCode), CairoFont.WhiteSmallText(), inputLabelBounds)
                    .AddItemSlotGrid(Inventory, DoSendPacket, inputColumns, inputSel, inputBounds, "inputslot");

            if (showProgressBar)
            {
                composer = progressBar.AddToComposer(composer, progressBounds);
            }

            SingleComposer = composer
                    .AddStaticText(Lang.Get(outputLabelLangCode), CairoFont.WhiteSmallText(), outputLabelBounds)
                    .AddItemSlotGrid(Inventory, DoSendPacket, outputColumns, outputSel, outputBounds, "outputslots")
                .EndChildElements()
                .Compose();
            if (showProgressBar) progressBar.Refresh(SingleComposer, true);
        }

        private static int[] SlotRange(int first, int last)
        {
            int[] slots = new int[last - first + 1];
            for (int i = 0; i < slots.Length; i++) slots[i] = first + i;
            return slots;
        }

        private static double ProgressBarX(double left, double rowWidth, double barWidth, string align)
        {
            return align == "left" ? left : left + (rowWidth - barWidth) / 2.0;
        }
    }
}
