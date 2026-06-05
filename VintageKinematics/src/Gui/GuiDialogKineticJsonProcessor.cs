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

        public override double DrawOrder => 0.2;

        public GuiDialogKineticJsonProcessor(string title, InventoryBase inv, BlockPos pos, int inputFirst, int inputLast, int outputFirst, int outputLast, ICoreClientAPI capi)
            : base(title, inv, pos, capi)
        {
            this.inputFirst = inputFirst;
            this.inputLast = inputLast;
            this.outputFirst = outputFirst;
            this.outputLast = outputLast;
            if (IsDuplicate) return;
            ComposeDialog(title);
        }

        private void ComposeDialog(string title)
        {
            double slotPad = GuiElementItemSlotGridBase.unscaledSlotPadding;
            double slotSize = GuiElementPassiveItemSlot.unscaledSlotSize;
            int inputCount = inputLast - inputFirst + 1;
            int outputCount = outputLast - outputFirst + 1;
            int inputColumns = Math.Min(Math.Max(inputCount, 1), 8);
            int outputColumns = Math.Min(Math.Max(outputCount, 1), 8);
            int inputRows = (inputCount + inputColumns - 1) / inputColumns;
            int outputRows = (outputCount + outputColumns - 1) / outputColumns;
            double rowWidth = Math.Max(Math.Max(inputColumns, outputColumns) * (slotSize + slotPad), 220.0);
            double topOffset = 16.0;

            ElementBounds inputLabelBounds = ElementBounds.Fixed(slotPad, slotPad + topOffset, rowWidth, 22.0);
            ElementBounds inputBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, slotPad, slotPad + topOffset + 24.0, inputColumns, inputRows);
            ElementBounds outputLabelBounds = ElementBounds.Fixed(slotPad, inputBounds.fixedY + inputBounds.fixedHeight + 8.0, rowWidth, 22.0);
            ElementBounds outputBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, slotPad, outputLabelBounds.fixedY + 24.0, outputColumns, outputRows);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(inputLabelBounds, inputBounds, outputLabelBounds, outputBounds);

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.RightMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0.0);

            int[] inputSel = SlotRange(inputFirst, inputLast);
            int[] outputSel = SlotRange(outputFirst, outputLast);

            SingleComposer = capi.Gui.CreateCompo("kineticjsonprocessor-" + BlockEntityPosition, dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(title, CloseIconPressed)
                .BeginChildElements(bgBounds)
                    .AddStaticText(Lang.Get("vintagekinematics:jsonprocessor-input"), CairoFont.WhiteSmallText(), inputLabelBounds)
                    .AddItemSlotGrid(Inventory, DoSendPacket, inputColumns, inputSel, inputBounds, "inputslot")
                    .AddStaticText(Lang.Get("vintagekinematics:jsonprocessor-outputs"), CairoFont.WhiteSmallText(), outputLabelBounds)
                    .AddItemSlotGrid(Inventory, DoSendPacket, outputColumns, outputSel, outputBounds, "outputslots")
                .EndChildElements()
                .Compose();
        }

        private static int[] SlotRange(int first, int last)
        {
            int[] slots = new int[last - first + 1];
            for (int i = 0; i < slots.Length; i++) slots[i] = first + i;
            return slots;
        }
    }
}
