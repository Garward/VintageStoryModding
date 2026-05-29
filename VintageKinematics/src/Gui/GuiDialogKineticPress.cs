using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Gui
{
    public class GuiDialogKineticPress : GuiDialogBlockEntity
    {
        public override double DrawOrder => 0.2;

        public GuiDialogKineticPress(string title, InventoryBase inventory, BlockPos pos, ICoreClientAPI capi)
            : base(title, inventory, pos, capi)
        {
            if (IsDuplicate) return;
            ComposeDialog(title);
        }

        private void ComposeDialog(string title)
        {
            double slotPad = GuiElementItemSlotGridBase.unscaledSlotPadding;
            double slotSize = GuiElementPassiveItemSlot.unscaledSlotSize;
            double rowWidth = System.Math.Max(3 * (slotSize + slotPad), 260.0);

            ElementBounds inputLabelBounds = ElementBounds.Fixed(slotPad, slotPad, rowWidth, 22.0);
            ElementBounds inputSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, slotPad, slotPad + 24.0, 1, 1);
            ElementBounds outputLabelBounds = ElementBounds.Fixed(slotPad, slotPad + 24.0 + inputSlotBounds.fixedHeight + 8.0, rowWidth, 22.0);
            ElementBounds outputSlotsBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, slotPad, outputLabelBounds.fixedY + 24.0, 3, 3);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(inputLabelBounds, inputSlotBounds, outputLabelBounds, outputSlotsBounds);

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.RightMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0.0);

            string inputLabel = Lang.Get("vintagekinematics:kineticextractor-input");
            string outputLabel = Lang.Get("vintagekinematics:kineticextractor-outputs");
            int[] inputSlot = new[] { 0 };
            int[] outputSlots = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 };

            SingleComposer = capi.Gui.CreateCompo("kineticextractor-" + BlockEntityPosition, dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(title, CloseIconPressed)
                .BeginChildElements(bgBounds)
                    .AddStaticText(inputLabel, CairoFont.WhiteSmallText(), inputLabelBounds)
                    .AddItemSlotGrid(Inventory, DoSendPacket, 1, inputSlot, inputSlotBounds, "inputslot")
                    .AddStaticText(outputLabel, CairoFont.WhiteSmallText(), outputLabelBounds)
                    .AddItemSlotGrid(Inventory, DoSendPacket, 3, outputSlots, outputSlotsBounds, "outputslots")
                .EndChildElements()
                .Compose();
        }
    }
}
