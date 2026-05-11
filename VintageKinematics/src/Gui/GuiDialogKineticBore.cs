using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Gui
{
    /// <summary>
    /// Bore dialog: top row is the 3-slot shaft input, below that the 3×3 output grid, then a
    /// status line and a Drill / Retract toggle button. The button label flips based on the
    /// current mode so a single control covers both directions of travel.
    /// </summary>
    public class GuiDialogKineticBore : GuiDialogBlockEntity
    {
        private readonly Func<bool> getRetracting;
        private readonly Func<bool> getHalted;
        private readonly Func<bool> getPaused;
        private readonly Func<int> getDepth;
        private readonly Action onToggleRetract;
        private const string ToggleButtonKey = "boreRetractToggle";
        private const string StatusTextKey = "boreStatusText";

        public override double DrawOrder => 0.2;

        public GuiDialogKineticBore(
            string title, InventoryBase inv, BlockPos pos,
            Func<bool> getRetracting, Func<bool> getHalted, Func<bool> getPaused, Func<int> getDepth,
            Action onToggleRetract, ICoreClientAPI capi)
            : base(title, inv, pos, capi)
        {
            this.getRetracting = getRetracting;
            this.getHalted = getHalted;
            this.getPaused = getPaused;
            this.getDepth = getDepth;
            this.onToggleRetract = onToggleRetract;
            if (IsDuplicate) return;
            ComposeDialog(title);
        }

        private void ComposeDialog(string title)
        {
            double slotPad = GuiElementItemSlotGridBase.unscaledSlotPadding;
            double slotSize = GuiElementPassiveItemSlot.unscaledSlotSize;
            double rowWidth = Math.Max(3 * (slotSize + slotPad), 260.0);

            ElementBounds inputLabelBounds = ElementBounds.Fixed(slotPad, slotPad, rowWidth, 22.0);
            ElementBounds inputSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, slotPad, slotPad + 24.0, 3, 1);
            ElementBounds outputLabelBounds = ElementBounds.Fixed(slotPad, inputSlotBounds.fixedY + inputSlotBounds.fixedHeight + 8.0, rowWidth, 22.0);
            ElementBounds outputSlotsBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, slotPad, outputLabelBounds.fixedY + 24.0, 3, 3);

            ElementBounds statusBounds = ElementBounds.Fixed(slotPad, outputSlotsBounds.fixedY + outputSlotsBounds.fixedHeight + 8.0, rowWidth, 22.0);
            ElementBounds btnBounds = ElementBounds.Fixed(slotPad, statusBounds.fixedY + 26.0, rowWidth, 28.0);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(inputLabelBounds, inputSlotBounds, outputLabelBounds, outputSlotsBounds, statusBounds, btnBounds);

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.RightMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0.0);

            string inputLabel = Lang.Get("vintagekinematics:bore-input");
            string outputLabel = Lang.Get("vintagekinematics:bore-outputs");
            int[] inputSlots = new[] { 0, 1, 2 };
            int[] outputSlots = new[] { 3, 4, 5, 6, 7, 8, 9, 10, 11 };

            SingleComposer = capi.Gui.CreateCompo("kineticbore-" + BlockEntityPosition, dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(title, CloseIconPressed)
                .BeginChildElements(bgBounds)
                    .AddStaticText(inputLabel, CairoFont.WhiteSmallText(), inputLabelBounds)
                    .AddItemSlotGrid(Inventory, DoSendPacket, 3, inputSlots, inputSlotBounds, "inputslots")
                    .AddStaticText(outputLabel, CairoFont.WhiteSmallText(), outputLabelBounds)
                    .AddItemSlotGrid(Inventory, DoSendPacket, 3, outputSlots, outputSlotsBounds, "outputslots")
                    .AddDynamicText(GetStatusText(), CairoFont.WhiteSmallText(), statusBounds, StatusTextKey)
                    .AddSmallButton(GetToggleLabel(), OnToggleClicked, btnBounds, EnumButtonStyle.Normal, ToggleButtonKey)
                .EndChildElements()
                .Compose();
        }

        private string GetToggleLabel()
        {
            bool retracting = getRetracting != null && getRetracting();
            bool paused = getPaused != null && getPaused();
            if (retracting) return Lang.Get("vintagekinematics:bore-stop-retract");
            if (paused) return Lang.Get("vintagekinematics:bore-start-drilling");
            return Lang.Get("vintagekinematics:bore-retract");
        }

        private string GetStatusText()
        {
            int depth = getDepth != null ? getDepth() : 0;
            bool retracting = getRetracting != null && getRetracting();
            bool paused = getPaused != null && getPaused();
            bool halted = getHalted != null && getHalted();
            if (retracting) return Lang.Get("vintagekinematics:bore-status-retracting", depth);
            if (paused) return Lang.Get("vintagekinematics:bore-status-paused", depth);
            if (halted) return Lang.Get("vintagekinematics:bore-status-halted", depth);
            return Lang.Get("vintagekinematics:bore-status-drilling", depth);
        }

        private bool OnToggleClicked()
        {
            onToggleRetract?.Invoke();
            return true;
        }

        public void OnStateUpdated()
        {
            if (SingleComposer == null) return;
            var btn = SingleComposer.GetButton(ToggleButtonKey);
            if (btn != null) btn.Text = GetToggleLabel();
            SingleComposer.GetDynamicText(StatusTextKey)?.SetNewText(GetStatusText());
        }
    }
}
