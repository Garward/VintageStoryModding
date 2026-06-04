using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Gui
{
    public class GuiDialogCoalMotor : GuiDialogBlockEntity
    {
        public override double DrawOrder => 0.2;

        public GuiDialogCoalMotor(string title, InventoryBase inventory, BlockPos pos, ICoreClientAPI capi)
            : base(title, inventory, pos, capi)
        {
            if (IsDuplicate) return;
            ComposeDialog(title);
        }

        private void ComposeDialog(string title)
        {
            double slotPad = GuiElementItemSlotGridBase.unscaledSlotPadding;
            double topOffset = 16.0;
            double rowWidth = 130.0;
            ElementBounds slotMeasure = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0.0, 0.0, 1, 1);
            double slotX = slotPad + (rowWidth - slotMeasure.fixedWidth) / 2.0;

            ElementBounds fuelLabelBounds = ElementBounds.Fixed(slotPad, slotPad + topOffset, rowWidth, 22.0);
            ElementBounds fuelSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, slotX, slotPad + topOffset + 24.0, 1, 1);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(fuelLabelBounds, fuelSlotBounds);

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.RightMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0.0);

            string fuelLabel = Lang.Get("vintagekinematics:coalmotor-fuel");
            int[] fuelSlot = new[] { 0 };

            SingleComposer = capi.Gui.CreateCompo("coalmotor-" + BlockEntityPosition, dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(title, CloseIconPressed)
                .BeginChildElements(bgBounds)
                    .AddStaticText(fuelLabel, CairoFont.WhiteSmallText(), EnumTextOrientation.Center, fuelLabelBounds)
                    .AddItemSlotGrid(Inventory, DoSendPacket, 1, fuelSlot, fuelSlotBounds, "fuelslot")
                .EndChildElements()
                .Compose();
        }
    }
}
