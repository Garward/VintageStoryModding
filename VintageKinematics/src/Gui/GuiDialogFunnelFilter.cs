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
        private readonly int slotCount;
        private const string ModeButtonKey = "filterModeToggle";
        private const string FuzzyButtonKey = "filterFuzzyToggle";

        public override double DrawOrder => 0.2;

        public GuiDialogFunnelFilter(
            string title, InventoryBase filterInv, BlockPos pos, int slotCount,
            Func<bool> getWhitelist, Func<bool> getFuzzy,
            Action onToggleMode, Action onToggleFuzzy,
            ICoreClientAPI capi)
            : base(title, filterInv, pos, capi)
        {
            this.getWhitelist = getWhitelist;
            this.getFuzzy = getFuzzy;
            this.onToggleMode = onToggleMode;
            this.onToggleFuzzy = onToggleFuzzy;
            this.slotCount = slotCount;
            if (IsDuplicate) return;
            ComposeDialog(title);
        }

        private void ComposeDialog(string title)
        {
            double slotPad = GuiElementItemSlotGridBase.unscaledSlotPadding;
            ElementBounds slotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, slotPad, slotPad, slotCount, 1);
            // Button width: at least 130px so labels like "Whitelist"/"Blacklist" don't get
            // re-sized by AutoBoxSize into something that drifts back over the slot row.
            double btnWidth = Math.Max(slotBounds.fixedWidth, 130.0);
            double btnY = slotPad + slotBounds.fixedHeight + 12.0;
            ElementBounds modeBounds = ElementBounds.Fixed(slotPad, btnY, btnWidth, 28.0);
            ElementBounds fuzzyBounds = ElementBounds.Fixed(slotPad, btnY + 36.0, btnWidth, 28.0);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(slotBounds, modeBounds, fuzzyBounds);

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.RightMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0.0);

            int[] selective = new int[slotCount];
            for (int i = 0; i < slotCount; i++) selective[i] = i;

            SingleComposer = capi.Gui.CreateCompo("funnelfilter-" + BlockEntityPosition, dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(title, CloseIconPressed)
                .BeginChildElements(bgBounds)
                    .AddItemSlotGrid(Inventory, DoSendPacket, slotCount, selective, slotBounds, "slotgrid")
                    .AddSmallButton(GetModeLabel(), OnToggleMode, modeBounds, EnumButtonStyle.Normal, ModeButtonKey)
                    .AddSmallButton(GetFuzzyLabel(), OnToggleFuzzy, fuzzyBounds, EnumButtonStyle.Normal, FuzzyButtonKey)
                .EndChildElements()
                .Compose();
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

        public void OnFilterStateUpdated()
        {
            if (SingleComposer == null) return;
            var modeBtn = SingleComposer.GetButton(ModeButtonKey);
            if (modeBtn != null) modeBtn.Text = GetModeLabel();
            var fuzzyBtn = SingleComposer.GetButton(FuzzyButtonKey);
            if (fuzzyBtn != null) fuzzyBtn.Text = GetFuzzyLabel();
            // Text setter only mutates the string; ReCompose rebakes the cached button textures.
            SingleComposer.ReCompose();
        }
    }
}
