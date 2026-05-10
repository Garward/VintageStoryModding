using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using VintageKinematics.Crafting;

namespace VintageKinematics.Gui
{
    /// <summary>
    /// Sawmill dialog: 1 input slot + 4 output slots in a row, plus a Plank/Shaft mode toggle.
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
            ElementBounds inputBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, slotPad, slotPad, 1, 1);
            ElementBounds arrowBounds = ElementBounds.Fixed(slotPad + inputBounds.fixedWidth + 6.0, slotPad + 12.0, 24.0, 24.0);
            double outputX = slotPad + inputBounds.fixedWidth + 6.0 + 24.0 + 6.0;
            ElementBounds outputBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, outputX, slotPad, 4, 1);

            double btnWidth = Math.Max(outputX + outputBounds.fixedWidth, 200.0);
            double btnY = slotPad + Math.Max(inputBounds.fixedHeight, outputBounds.fixedHeight) + 12.0;
            ElementBounds modeBounds = ElementBounds.Fixed(slotPad, btnY, btnWidth, 28.0);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(inputBounds, outputBounds, modeBounds);

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.RightMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0.0);

            int[] inputSel = new[] { 0 };
            int[] outputSel = new[] { 1, 2, 3, 4 };

            SingleComposer = capi.Gui.CreateCompo("kineticsawmill-" + BlockEntityPosition, dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(title, CloseIconPressed)
                .BeginChildElements(bgBounds)
                    .AddItemSlotGrid(Inventory, DoSendPacket, 1, inputSel, inputBounds, "inputslot")
                    .AddItemSlotGrid(Inventory, DoSendPacket, 4, outputSel, outputBounds, "outputslots")
                    .AddSmallButton(GetModeLabel(), OnToggleMode, modeBounds, EnumButtonStyle.Normal, ModeButtonKey)
                .EndChildElements()
                .Compose();
        }

        private string GetModeLabel()
        {
            SawmillMode mode = getMode != null ? getMode() : SawmillMode.Plank;
            return mode == SawmillMode.Shaft
                ? Lang.Get("vintagekinematics:kineticsawmill-mode-shaft")
                : Lang.Get("vintagekinematics:kineticsawmill-mode-plank");
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
