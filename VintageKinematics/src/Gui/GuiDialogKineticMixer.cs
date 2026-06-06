using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Gui
{
    public class GuiDialogKineticMixer : GuiDialogBlockEntity
    {
        private readonly MachineProgressBar progressBar;

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
            progressBar = new MachineProgressBar(capi, "mixer-progress", getProgress, getProgressMax, getCanProgress);
            if (IsDuplicate) return;
            ComposeDialog(title);
        }

        public override void OnRenderGUI(float deltaTime)
        {
            progressBar.Refresh(SingleComposer, false);
            base.OnRenderGUI(deltaTime);
        }

        private void ComposeDialog(string title)
        {
            double slotPad = GuiElementItemSlotGridBase.unscaledSlotPadding;
            double slotSize = GuiElementPassiveItemSlot.unscaledSlotSize;
            double rowWidth = System.Math.Max(3 * (slotSize + slotPad), 260.0);
            double topOffset = 16.0;

            ElementBounds inputLabelBounds = ElementBounds.Fixed(slotPad, slotPad + topOffset, rowWidth, 22.0);
            ElementBounds inputSlotsBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, slotPad, slotPad + topOffset + 24.0, 3, 3);
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

            GuiComposer composer = capi.Gui.CreateCompo("kineticmixer-" + BlockEntityPosition, dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(title, CloseIconPressed)
                .BeginChildElements(bgBounds)
                    .AddStaticText(inputLabel, CairoFont.WhiteSmallText(), inputLabelBounds)
                    .AddItemSlotGrid(Inventory, DoSendPacket, 3, inputSlots, inputSlotsBounds, "inputslots");

            SingleComposer = progressBar.AddToComposer(composer, progressBounds)
                    .AddStaticText(outputLabel, CairoFont.WhiteSmallText(), outputLabelBounds)
                    .AddItemSlotGrid(Inventory, DoSendPacket, 3, outputSlots, outputSlotsBounds, "outputslots")
                .EndChildElements()
                .Compose();
            progressBar.Refresh(SingleComposer, true);
        }
    }
}
