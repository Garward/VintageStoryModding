using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using VintageKinematics.Crafting;

namespace VintageKinematics.Gui
{
    /// <summary>
    /// Sawmill dialog: 1 input slot + output slots, plus a recipe browser that selects the sawmill mode.
    /// </summary>
    public class GuiDialogKineticSawmill : GuiDialogBlockEntity
    {
        private readonly Func<SawmillMode> getMode;
        private readonly Action<SawmillMode> onSelectMode;
        private readonly MachineRecipeBrowser<SawmillRecipeListItem> recipeBrowser;
        private readonly MachineProgressBar progressBar;
        private const string RecipeButtonKey = "sawmillRecipeBrowser";

        public override double DrawOrder => 0.2;

        public GuiDialogKineticSawmill(
            string title, InventoryBase inv, BlockPos pos,
            Func<SawmillMode> getMode, Action<SawmillMode> onSelectMode,
            Func<float> getProgress, Func<float> getProgressMax, Func<bool> getCanProgress,
            ICoreClientAPI capi)
            : base(title, inv, pos, capi)
        {
            this.getMode = getMode;
            this.onSelectMode = onSelectMode;
            progressBar = new MachineProgressBar(capi, "sawmill-progress", getProgress, getProgressMax, getCanProgress);
            recipeBrowser = new MachineRecipeBrowser<SawmillRecipeListItem>(
                "sawmill-recipe",
                "vintagekinematics:kineticsawmill-recipes",
                "vintagekinematics:kineticsawmill-search-recipes",
                BuildRecipeListItems,
                OnRecipeClicked,
                () => SingleComposer,
                width: 500.0,
                listHeight: 292.0,
                cellHeight: 64,
                filterValues: SawmillFilterValues(),
                filterNames: SawmillFilterNames(),
                filterMatches: (item, filter) => item?.Recipe != null && ModeFilterCode(item.Recipe.Mode) == filter);
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
            double rowWidth = Math.Max(3 * (slotSize + slotPad), 260.0);
            double topOffset = 16.0;

            ElementBounds inputLabelBounds = ElementBounds.Fixed(slotPad, slotPad + topOffset, rowWidth, 22.0);
            ElementBounds inputBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, slotPad, slotPad + topOffset + 24.0, 1, 1);
            double progressBarWidth = Math.Min(rowWidth, 144.0);
            ElementBounds progressBounds = ElementBounds.Fixed(slotPad, inputBounds.fixedY + inputBounds.fixedHeight + 8.0, progressBarWidth, 18.0);
            ElementBounds outputLabelBounds = ElementBounds.Fixed(slotPad, progressBounds.fixedY + progressBounds.fixedHeight + 10.0, rowWidth, 22.0);
            ElementBounds outputBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, slotPad, outputLabelBounds.fixedY + 24.0, 3, 3);
            ElementBounds recipeButtonBounds = ElementBounds.Fixed(slotPad, outputBounds.fixedY + outputBounds.fixedHeight + 12.0, rowWidth, 28.0);
            recipeBrowser.SetBounds(slotPad + rowWidth + 24.0, slotPad + topOffset);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            List<ElementBounds> childBounds = new List<ElementBounds>
            {
                inputLabelBounds,
                inputBounds,
                progressBounds,
                outputLabelBounds,
                outputBounds,
                recipeButtonBounds
            };
            recipeBrowser.AddBounds(childBounds);
            bgBounds.WithChildren(childBounds.ToArray());

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.RightMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0.0);

            string inputLabel = Lang.Get("vintagekinematics:kineticsawmill-input");
            string outputLabel = Lang.Get("vintagekinematics:kineticsawmill-outputs");
            int[] inputSel = new[] { 0 };
            int[] outputSel = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 };

            GuiComposer composer = capi.Gui.CreateCompo("kineticsawmill-" + BlockEntityPosition, dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(title, CloseIconPressed)
                .BeginChildElements(bgBounds)
                    .AddStaticText(inputLabel, CairoFont.WhiteSmallText(), inputLabelBounds)
                    .AddItemSlotGrid(Inventory, DoSendPacket, 1, inputSel, inputBounds, "inputslot");

            composer = progressBar.AddToComposer(composer, progressBounds)
                    .AddStaticText(outputLabel, CairoFont.WhiteSmallText(), outputLabelBounds)
                    .AddItemSlotGrid(Inventory, DoSendPacket, 3, outputSel, outputBounds, "outputslots")
                    .AddSmallButton(GetModeLabel(), OnToggleRecipeBrowser, recipeButtonBounds, EnumButtonStyle.Normal, RecipeButtonKey);

            composer = recipeBrowser.AddToComposer(composer);

            GuiComposer oldComposer = SingleComposer;
            SingleComposer = composer
                .EndChildElements()
                .Compose();
            oldComposer?.Dispose();
            recipeBrowser.AfterCompose(SingleComposer);
            progressBar.Refresh(SingleComposer, true);
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
                SawmillMode.Gearbox => Lang.Get("vintagekinematics:kineticsawmill-mode-gearbox"),
                _ => Lang.Get("vintagekinematics:kineticsawmill-mode-plank")
            };
        }

        private bool OnToggleRecipeBrowser()
        {
            return recipeBrowser.Toggle(() => ComposeDialog(DialogTitle));
        }

        private void OnRecipeClicked(SawmillRecipeListItem item)
        {
            if (item?.Recipe == null) return;
            onSelectMode?.Invoke(item.Recipe.Mode);
        }

        public override void Dispose()
        {
            recipeBrowser?.Dispose();
            base.Dispose();
        }

        private List<SawmillRecipeListItem> BuildRecipeListItems()
        {
            var registry = capi.ModLoader.GetModSystem<KineticSawmillRecipeRegistry>();
            List<SawmillRecipeListItem> items = new List<SawmillRecipeListItem>();
            if (registry == null) return items;

            foreach (KineticSawmillRecipe recipe in registry.Recipes)
            {
                items.Add(new SawmillRecipeListItem(recipe, capi));
            }

            return items;
        }

        private static string[] SawmillFilterValues()
        {
            return new[] { "", "plank", "shaft", "stick", "cogsection", "firewood", "gearbox" };
        }

        private static string[] SawmillFilterNames()
        {
            return new[]
            {
                Lang.Get("vintagekinematics:kineticsawmill-filter-all"),
                Lang.Get("vintagekinematics:kineticsawmill-filter-plank"),
                Lang.Get("vintagekinematics:kineticsawmill-filter-shaft"),
                Lang.Get("vintagekinematics:kineticsawmill-filter-stick"),
                Lang.Get("vintagekinematics:kineticsawmill-filter-cogsection"),
                Lang.Get("vintagekinematics:kineticsawmill-filter-firewood"),
                Lang.Get("vintagekinematics:kineticsawmill-filter-gearbox")
            };
        }

        private static string ModeFilterCode(SawmillMode mode)
        {
            return mode switch
            {
                SawmillMode.Shaft => "shaft",
                SawmillMode.Stick => "stick",
                SawmillMode.CogwheelSection => "cogsection",
                SawmillMode.Firewood => "firewood",
                SawmillMode.Gearbox => "gearbox",
                _ => "plank"
            };
        }

        public void OnModeUpdated()
        {
            if (SingleComposer == null) return;
            var btn = SingleComposer.GetButton(RecipeButtonKey);
            if (btn != null) btn.Text = GetModeLabel();
            SingleComposer.ReCompose();
        }
    }
}
