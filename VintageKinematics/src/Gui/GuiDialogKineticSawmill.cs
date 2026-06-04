using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using VintageKinematics.Crafting;

namespace VintageKinematics.Gui
{
    /// <summary>
    /// Sawmill dialog: 1 input slot + output slots, plus a sawmill mode toggle.
    /// Mirrors the funnel filter dialog pattern — toggle flips locally for instant feedback,
    /// then forwards to the BE via a custom packet.
    /// </summary>
    public class GuiDialogKineticSawmill : GuiDialogBlockEntity
    {
        private readonly Func<SawmillMode> getMode;
        private readonly Action onToggleMode;
        private const string ModeButtonKey = "sawmillModeToggle";

        public override double DrawOrder => 0.2;

        public GuiDialogKineticSawmill(
            string title, InventoryBase inv, BlockPos pos,
            Func<SawmillMode> getMode, Action onToggleMode,
            ICoreClientAPI capi)
            : base(title, inv, pos, capi)
        {
            this.getMode = getMode;
            this.onToggleMode = onToggleMode;
            if (IsDuplicate) return;
            ComposeDialog(title);
        }

        private void ComposeDialog(string title)
        {
            double slotPad = GuiElementItemSlotGridBase.unscaledSlotPadding;
            double slotSize = GuiElementPassiveItemSlot.unscaledSlotSize;
            double rowWidth = Math.Max(3 * (slotSize + slotPad), 260.0);
            double topOffset = 16.0;

            ElementBounds inputLabelBounds = ElementBounds.Fixed(slotPad, slotPad + topOffset, rowWidth, 22.0);
            ElementBounds inputBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, slotPad, slotPad + topOffset + 24.0, 1, 1);
            ElementBounds outputLabelBounds = ElementBounds.Fixed(slotPad, inputBounds.fixedY + inputBounds.fixedHeight + 8.0, rowWidth, 22.0);
            ElementBounds outputBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, slotPad, outputLabelBounds.fixedY + 24.0, 3, 3);
            ElementBounds modeBounds = ElementBounds.Fixed(slotPad, outputBounds.fixedY + outputBounds.fixedHeight + 12.0, rowWidth, 28.0);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(inputLabelBounds, inputBounds, outputLabelBounds, outputBounds, modeBounds);

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.RightMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0.0);

            string inputLabel = Lang.Get("vintagekinematics:kineticsawmill-input");
            string outputLabel = Lang.Get("vintagekinematics:kineticsawmill-outputs");
            int[] inputSel = new[] { 0 };
            int[] outputSel = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 };

            SingleComposer = capi.Gui.CreateCompo("kineticsawmill-" + BlockEntityPosition, dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(title, CloseIconPressed)
                .BeginChildElements(bgBounds)
                    .AddStaticText(inputLabel, CairoFont.WhiteSmallText(), inputLabelBounds)
                    .AddItemSlotGrid(Inventory, DoSendPacket, 1, inputSel, inputBounds, "inputslot")
                    .AddStaticText(outputLabel, CairoFont.WhiteSmallText(), outputLabelBounds)
                    .AddItemSlotGrid(Inventory, DoSendPacket, 3, outputSel, outputBounds, "outputslots")
                    .AddSmallButton(GetModeLabel(), OnToggleMode, modeBounds, EnumButtonStyle.Normal, ModeButtonKey)
                .EndChildElements()
                .Compose();
        }

        private string GetModeLabel()
        {
            SawmillMode mode = getMode != null ? getMode() : SawmillMode.Plank;
            return mode switch
            {
                SawmillMode.Shaft => Lang.Get("vintagekinematics:kineticsawmill-mode-shaft"),
                SawmillMode.Stick => Lang.Get("vintagekinematics:kineticsawmill-mode-stick"),
                SawmillMode.CogwheelSection => Lang.Get("vintagekinematics:kineticsawmill-mode-cogsection"),
                SawmillMode.Firewood => Lang.Get("vintagekinematics:kineticsawmill-mode-firewood"),
                _ => Lang.Get("vintagekinematics:kineticsawmill-mode-plank")
            };
        }

        private bool OnToggleMode()
        {
            onToggleMode?.Invoke();
            return true;
        }

        public void OnModeUpdated()
        {
            if (SingleComposer == null) return;
            var btn = SingleComposer.GetButton(ModeButtonKey);
            if (btn != null) btn.Text = GetModeLabel();
            SingleComposer.ReCompose();
        }
    }
}
