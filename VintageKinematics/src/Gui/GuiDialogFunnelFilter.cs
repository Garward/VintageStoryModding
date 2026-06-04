using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Gui
{
    // Custom dialog: 1 fake filter slot + Whitelist/Blacklist and Exact/Fuzzy toggle buttons.
    // Slot uses standard inventory networking for visual feedback; the BE forwards the actual
    // filter mutation via a custom C→S packet so the server side stays in sync.
    public class GuiDialogFunnelFilter : GuiDialogBlockEntity
    {
        private readonly Func<bool> getWhitelist;
        private readonly Func<bool> getFuzzy;
        private readonly Action onToggleMode;
        private readonly Action onToggleFuzzy;
        private readonly Func<string> getPulseLabel;
        private readonly Action onTogglePulse;
        private readonly int slotCount;
        private readonly bool centerSingleSlot;
        private const string ModeButtonKey = "filterModeToggle";
        private const string FuzzyButtonKey = "filterFuzzyToggle";
        private const string PulseButtonKey = "filterPulseToggle";

        public override double DrawOrder => 0.2;

        public GuiDialogFunnelFilter(
            string title, InventoryBase filterInv, BlockPos pos, int slotCount,
            Func<bool> getWhitelist, Func<bool> getFuzzy,
            Action onToggleMode, Action onToggleFuzzy,
            ICoreClientAPI capi,
            Func<string> getPulseLabel = null,
            Action onTogglePulse = null,
            bool centerSingleSlot = false)
            : base(title, filterInv, pos, capi)
        {
            this.getWhitelist = getWhitelist;
            this.getFuzzy = getFuzzy;
            this.onToggleMode = onToggleMode;
            this.onToggleFuzzy = onToggleFuzzy;
            this.getPulseLabel = getPulseLabel;
            this.onTogglePulse = onTogglePulse;
            this.slotCount = slotCount;
            this.centerSingleSlot = centerSingleSlot;
            if (IsDuplicate) return;
            ComposeDialog(title);
        }

        private void ComposeDialog(string title)
        {
            double slotPad = GuiElementItemSlotGridBase.unscaledSlotPadding;
            ElementBounds slotMeasure = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 0, slotCount, 1);
            // Button width: at least 130px so labels like "Whitelist"/"Blacklist" don't get
            // re-sized by AutoBoxSize into something that drifts back over the slot row.
            double btnWidth = Math.Max(slotMeasure.fixedWidth, 130.0);
            bool centeredSlot = centerSingleSlot && slotCount == 1;
            double slotX = centeredSlot ? slotPad + (btnWidth - slotMeasure.fixedWidth) / 2.0 : slotPad;
            double slotY = centeredSlot ? slotPad + 28.0 : slotPad + 12.0;
            ElementBounds slotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, slotX, slotY, slotCount, 1);
            double btnY = slotY + slotBounds.fixedHeight + (centeredSlot ? 8.0 : 12.0);
            ElementBounds modeBounds = ElementBounds.Fixed(slotPad, btnY, btnWidth, 28.0);
            double buttonStep = 36.0;
            ElementBounds fuzzyBounds = ElementBounds.Fixed(slotPad, btnY + buttonStep, btnWidth, 28.0);
            ElementBounds pulseBounds = ElementBounds.Fixed(slotPad, btnY + buttonStep * 2.0, btnWidth, 28.0);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            if (onTogglePulse != null)
            {
                bgBounds.WithChildren(slotBounds, modeBounds, fuzzyBounds, pulseBounds);
            }
            else
            {
                bgBounds.WithChildren(slotBounds, modeBounds, fuzzyBounds);
            }

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.RightMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0.0);

            int[] selective = new int[slotCount];
            for (int i = 0; i < slotCount; i++) selective[i] = i;

            var composer = capi.Gui.CreateCompo("funnelfilter-" + BlockEntityPosition, dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(title, CloseIconPressed)
                .BeginChildElements(bgBounds)
                    .AddItemSlotGrid(Inventory, DoSendPacket, slotCount, selective, slotBounds, "slotgrid")
                    .AddSmallButton(GetModeLabel(), OnToggleMode, modeBounds, EnumButtonStyle.Normal, ModeButtonKey)
                    .AddSmallButton(GetFuzzyLabel(), OnToggleFuzzy, fuzzyBounds, EnumButtonStyle.Normal, FuzzyButtonKey);

            if (onTogglePulse != null)
            {
                composer.AddSmallButton(GetPulseLabel(), OnTogglePulse, pulseBounds, EnumButtonStyle.Normal, PulseButtonKey);
            }

            SingleComposer = composer.EndChildElements().Compose();
        }

        private string GetModeLabel()
        {
            return getWhitelist != null && getWhitelist()
                ? Lang.Get("vintagekinematics:funnel-filter-whitelist")
                : Lang.Get("vintagekinematics:funnel-filter-blacklist");
        }

        private string GetFuzzyLabel()
        {
            return getFuzzy != null && getFuzzy()
                ? Lang.Get("vintagekinematics:funnel-filter-fuzzy")
                : Lang.Get("vintagekinematics:funnel-filter-exact");
        }

        private bool OnToggleMode()
        {
            onToggleMode?.Invoke();
            return true;
        }

        private bool OnToggleFuzzy()
        {
            onToggleFuzzy?.Invoke();
            return true;
        }

        private string GetPulseLabel()
        {
            return getPulseLabel?.Invoke() ?? "";
        }

        private bool OnTogglePulse()
        {
            onTogglePulse?.Invoke();
            return true;
        }

        public void OnFilterStateUpdated()
        {
            if (SingleComposer == null) return;
            var modeBtn = SingleComposer.GetButton(ModeButtonKey);
            if (modeBtn != null) modeBtn.Text = GetModeLabel();
            var fuzzyBtn = SingleComposer.GetButton(FuzzyButtonKey);
            if (fuzzyBtn != null) fuzzyBtn.Text = GetFuzzyLabel();
            var pulseBtn = SingleComposer.GetButton(PulseButtonKey);
            if (pulseBtn != null) pulseBtn.Text = GetPulseLabel();
            // Text setter only mutates the string; ReCompose rebakes the cached button textures.
            SingleComposer.ReCompose();
        }
    }
}
