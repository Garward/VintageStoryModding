using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Gui
{
    public class GuiDialogGeothermalBore : GuiDialogBlockEntity
    {
        private readonly Func<bool> getRetracting;
        private readonly Func<bool> getHalted;
        private readonly Func<bool> getPaused;
        private readonly Func<bool> getTapped;
        private readonly Func<int> getDepth;
        private readonly Action onToggleRetract;
        private const string ToggleButtonKey = "geothermalBoreRetractToggle";
        private const string StatusTextKey = "geothermalBoreStatusText";

        public override double DrawOrder => 0.2;

        public GuiDialogGeothermalBore(
            string title, InventoryBase inv, BlockPos pos,
            Func<bool> getRetracting, Func<bool> getHalted, Func<bool> getPaused,
            Func<bool> getTapped, Func<int> getDepth,
            Action onToggleRetract, ICoreClientAPI capi)
            : base(title, inv, pos, capi)
        {
            this.getRetracting = getRetracting;
            this.getHalted = getHalted;
            this.getPaused = getPaused;
            this.getTapped = getTapped;
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
            double topOffset = 16.0;

            ElementBounds rodLabelBounds = ElementBounds.Fixed(slotPad, slotPad + topOffset, rowWidth, 22.0);
            ElementBounds rodSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, slotPad, slotPad + topOffset + 24.0, 3, 1);
            ElementBounds pipeLabelBounds = ElementBounds.Fixed(slotPad, rodSlotBounds.fixedY + rodSlotBounds.fixedHeight + 8.0, rowWidth, 22.0);
            ElementBounds pipeSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, slotPad, pipeLabelBounds.fixedY + 24.0, 3, 1);
            ElementBounds statusBounds = ElementBounds.Fixed(slotPad, pipeSlotBounds.fixedY + pipeSlotBounds.fixedHeight + 8.0, rowWidth, 22.0);
            ElementBounds btnBounds = ElementBounds.Fixed(slotPad, statusBounds.fixedY + 26.0, rowWidth, 28.0);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(rodLabelBounds, rodSlotBounds, pipeLabelBounds, pipeSlotBounds, statusBounds, btnBounds);

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.RightMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0.0);

            int[] rodSlots = new[] { 0, 1, 2 };
            int[] pipeSlots = new[] { 3, 4, 5 };

            SingleComposer = capi.Gui.CreateCompo("geothermalbore-" + BlockEntityPosition, dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(title, CloseIconPressed)
                .BeginChildElements(bgBounds)
                    .AddStaticText(Lang.Get("vintagekinematics:geothermalbore-rods"), CairoFont.WhiteSmallText(), rodLabelBounds)
                    .AddItemSlotGrid(Inventory, DoSendPacket, 3, rodSlots, rodSlotBounds, "rodslots")
                    .AddStaticText(Lang.Get("vintagekinematics:geothermalbore-pipes"), CairoFont.WhiteSmallText(), pipeLabelBounds)
                    .AddItemSlotGrid(Inventory, DoSendPacket, 3, pipeSlots, pipeSlotBounds, "pipeslots")
                    .AddDynamicText(GetStatusText(), CairoFont.WhiteSmallText(), statusBounds, StatusTextKey)
                    .AddSmallButton(GetToggleLabel(), OnToggleClicked, btnBounds, EnumButtonStyle.Normal, ToggleButtonKey)
                .EndChildElements()
                .Compose();
        }

        private string GetToggleLabel()
        {
            bool retracting = getRetracting != null && getRetracting();
            bool paused = getPaused != null && getPaused();
            bool tapped = getTapped != null && getTapped();
            if (retracting) return Lang.Get("vintagekinematics:bore-stop-retract");
            if (tapped) return Lang.Get("vintagekinematics:bore-retract");
            if (paused) return Lang.Get("vintagekinematics:bore-start-drilling");
            return Lang.Get("vintagekinematics:bore-retract");
        }

        private string GetStatusText()
        {
            int depth = getDepth != null ? getDepth() : 0;
            bool tapped = getTapped != null && getTapped();
            bool retracting = getRetracting != null && getRetracting();
            bool paused = getPaused != null && getPaused();
            bool halted = getHalted != null && getHalted();
            if (tapped) return Lang.Get("vintagekinematics:geothermalbore-status-tapped", depth);
            if (retracting) return Lang.Get("vintagekinematics:geothermalbore-status-retracting", depth);
            if (paused) return Lang.Get("vintagekinematics:geothermalbore-status-paused", depth);
            if (halted) return Lang.Get("vintagekinematics:geothermalbore-status-halted", depth);
            return Lang.Get("vintagekinematics:geothermalbore-status-drilling", depth);
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
