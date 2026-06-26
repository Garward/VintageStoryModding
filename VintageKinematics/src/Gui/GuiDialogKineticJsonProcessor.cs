using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using VintageKinematics.Crafting;

namespace VintageKinematics.Gui
{
    public class GuiDialogKineticJsonProcessor : GuiDialogBlockEntity
    {
        private readonly int inputFirst;
        private readonly int inputLast;
        private readonly int outputFirst;
        private readonly int outputLast;
        private readonly bool showProgressBar;
        private readonly double progressBarWidth;
        private readonly string progressBarAlign;
        private readonly int inputColumnsOverride;
        private readonly int outputColumnsOverride;
        private readonly string inputLabelLangCode;
        private readonly string outputLabelLangCode;
        private readonly string dialogKeyPrefix;
        private readonly string machineCode;
        private readonly string recipeSource;
        private readonly bool showRecipeBrowser;
        private readonly MachineProgressBar progressBar;
        private readonly Func<IEnumerable<IRecipeBrowserListItem>> buildRecipeItems;
        private readonly Action<IRecipeBrowserListItem> onRecipeClicked;
        private readonly Func<string> recipeButtonLabel;
        private readonly string[] recipeSortValues;
        private readonly string[] recipeSortNames;
        private readonly int recipeBrowserCellHeight;
        private readonly MachineRecipeBrowser<IRecipeBrowserListItem> recipeBrowser;
        private const string RecipeButtonKey = "jsonprocessor-recipes";

        public override double DrawOrder => 0.2;

        public GuiDialogKineticJsonProcessor(
            string title,
            InventoryBase inv,
            BlockPos pos,
            int inputFirst,
            int inputLast,
            int outputFirst,
            int outputLast,
            bool showProgressBar,
            double progressBarWidth,
            string progressBarAlign,
            Func<float> getProgress,
            Func<float> getProgressMax,
            Func<bool> getCanProgress,
            ICoreClientAPI capi,
            int inputColumnsOverride = 0,
            int outputColumnsOverride = 0,
            string inputLabelLangCode = null,
            string outputLabelLangCode = null,
            string dialogKeyPrefix = "kineticjsonprocessor",
            string machineCode = null,
            string recipeSource = null,
            bool showRecipeBrowser = false,
            string recipeTitleLangCode = null,
            string recipeSearchLangCode = null,
            double recipeBrowserWidth = 500.0,
            double recipeBrowserListHeight = 292.0,
            Func<IEnumerable<IRecipeBrowserListItem>> buildRecipeItems = null,
            Action<IRecipeBrowserListItem> onRecipeClicked = null,
            Func<string> recipeButtonLabel = null,
            string[] recipeSortValues = null,
            string[] recipeSortNames = null,
            int recipeBrowserCellHeight = 64)
            : base(title, inv, pos, capi)
        {
            this.inputFirst = inputFirst;
            this.inputLast = inputLast;
            this.outputFirst = outputFirst;
            this.outputLast = outputLast;
            this.showProgressBar = showProgressBar;
            this.progressBarWidth = progressBarWidth;
            this.progressBarAlign = progressBarAlign;
            this.inputColumnsOverride = inputColumnsOverride;
            this.outputColumnsOverride = outputColumnsOverride;
            this.inputLabelLangCode = inputLabelLangCode ?? "vintagekinematics:jsonprocessor-input";
            this.outputLabelLangCode = outputLabelLangCode ?? "vintagekinematics:jsonprocessor-outputs";
            this.dialogKeyPrefix = dialogKeyPrefix ?? "kineticjsonprocessor";
            this.machineCode = machineCode;
            this.recipeSource = string.IsNullOrEmpty(recipeSource) ? "process" : recipeSource;
            this.showRecipeBrowser = showRecipeBrowser;
            this.buildRecipeItems = buildRecipeItems;
            this.onRecipeClicked = onRecipeClicked;
            this.recipeButtonLabel = recipeButtonLabel;
            this.recipeSortValues = recipeSortValues ?? RecipeSortValues();
            this.recipeSortNames = recipeSortNames ?? RecipeSortNames();
            this.recipeBrowserCellHeight = recipeBrowserCellHeight;
            progressBar = new MachineProgressBar(capi, this.dialogKeyPrefix + "-progress", getProgress, getProgressMax, getCanProgress);
            if (showRecipeBrowser)
            {
                recipeBrowser = new MachineRecipeBrowser<IRecipeBrowserListItem>(
                    this.dialogKeyPrefix + "-recipe",
                    recipeTitleLangCode ?? "vintagekinematics:jsonprocessor-recipes",
                    recipeSearchLangCode ?? "vintagekinematics:jsonprocessor-search-recipes",
                    BuildRecipeListItems,
                    this.onRecipeClicked,
                    () => SingleComposer,
                    width: recipeBrowserWidth,
                    listHeight: recipeBrowserListHeight,
                    cellHeight: this.recipeBrowserCellHeight,
                    sortValues: this.recipeSortValues,
                    sortNames: this.recipeSortNames);
            }
            if (IsDuplicate) return;
            ComposeDialog(title);
        }

        public override void OnRenderGUI(float deltaTime)
        {
            if (showProgressBar) progressBar.Refresh(SingleComposer, false);
            base.OnRenderGUI(deltaTime);
        }

        private void ComposeDialog(string title)
        {
            double slotPad = GuiElementItemSlotGridBase.unscaledSlotPadding;
            double slotSize = GuiElementPassiveItemSlot.unscaledSlotSize;
            int inputCount = inputLast - inputFirst + 1;
            int outputCount = outputLast - outputFirst + 1;
            int inputColumns = inputColumnsOverride > 0 ? Math.Min(inputColumnsOverride, inputCount) : Math.Min(Math.Max(inputCount, 1), 8);
            int outputColumns = outputColumnsOverride > 0 ? Math.Min(outputColumnsOverride, outputCount) : Math.Min(Math.Max(outputCount, 1), 8);
            int inputRows = (inputCount + inputColumns - 1) / inputColumns;
            int outputRows = (outputCount + outputColumns - 1) / outputColumns;
            double rowWidth = Math.Max(Math.Max(inputColumns, outputColumns) * (slotSize + slotPad), 220.0);
            double topOffset = 16.0;

            ElementBounds inputLabelBounds = ElementBounds.Fixed(slotPad, slotPad + topOffset, rowWidth, 22.0);
            ElementBounds inputBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, slotPad, slotPad + topOffset + 24.0, inputColumns, inputRows);
            double barWidth = Math.Min(rowWidth, Math.Max(48.0, progressBarWidth));
            double barX = ProgressBarX(slotPad, rowWidth, barWidth, progressBarAlign);
            ElementBounds progressBounds = ElementBounds.Fixed(barX, inputBounds.fixedY + inputBounds.fixedHeight + 8.0, barWidth, 18.0);
            double outputLabelY = showProgressBar ? progressBounds.fixedY + progressBounds.fixedHeight + 10.0 : inputBounds.fixedY + inputBounds.fixedHeight + 8.0;
            ElementBounds outputLabelBounds = ElementBounds.Fixed(slotPad, outputLabelY, rowWidth, 22.0);
            ElementBounds outputBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, slotPad, outputLabelBounds.fixedY + 24.0, outputColumns, outputRows);
            ElementBounds recipeButtonBounds = showRecipeBrowser
                ? ElementBounds.Fixed(slotPad, outputBounds.fixedY + outputBounds.fixedHeight + 12.0, rowWidth, 28.0)
                : null;
            recipeBrowser?.SetBounds(slotPad + rowWidth + 24.0, slotPad + topOffset);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            List<ElementBounds> childBounds = new List<ElementBounds>();
            childBounds.Add(inputLabelBounds);
            childBounds.Add(inputBounds);
            if (showProgressBar)
            {
                childBounds.Add(progressBounds);
            }
            childBounds.Add(outputLabelBounds);
            childBounds.Add(outputBounds);
            if (showRecipeBrowser) childBounds.Add(recipeButtonBounds);
            recipeBrowser?.AddBounds(childBounds);
            bgBounds.WithChildren(childBounds.ToArray());

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.RightMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0.0);

            int[] inputSel = SlotRange(inputFirst, inputLast);
            int[] outputSel = SlotRange(outputFirst, outputLast);

            GuiComposer oldComposer = SingleComposer;
            GuiComposer composer = capi.Gui.CreateCompo(dialogKeyPrefix + "-" + BlockEntityPosition, dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(title, CloseIconPressed)
                .BeginChildElements(bgBounds)
                    .AddStaticText(Lang.Get(inputLabelLangCode), CairoFont.WhiteSmallText(), inputLabelBounds)
                    .AddItemSlotGrid(Inventory, DoSendPacket, inputColumns, inputSel, inputBounds, "inputslot");

            if (showProgressBar)
            {
                composer = progressBar.AddToComposer(composer, progressBounds);
            }

            composer = composer
                .AddStaticText(Lang.Get(outputLabelLangCode), CairoFont.WhiteSmallText(), outputLabelBounds)
                .AddItemSlotGrid(Inventory, DoSendPacket, outputColumns, outputSel, outputBounds, "outputslots");
            if (showRecipeBrowser)
            {
                composer = composer.AddSmallButton(GetRecipeButtonLabel(), OnToggleRecipeBrowser, recipeButtonBounds, EnumButtonStyle.Normal, RecipeButtonKey);
                composer = recipeBrowser.AddToComposer(composer);
            }

            SingleComposer = composer
                    .EndChildElements()
                    .Compose();
            oldComposer?.Dispose();
            recipeBrowser?.AfterCompose(SingleComposer);
            if (showProgressBar) progressBar.Refresh(SingleComposer, true);
        }

        private bool OnToggleRecipeBrowser()
        {
            return recipeBrowser?.Toggle(() => ComposeDialog(DialogTitle)) == true;
        }

        public void RefreshRecipeButtonLabel()
        {
            if (SingleComposer == null || !showRecipeBrowser) return;
            GuiElementTextButton button = SingleComposer.GetButton(RecipeButtonKey);
            if (button == null) return;
            button.Text = GetRecipeButtonLabel();
            SingleComposer.ReCompose();
        }

        private string GetRecipeButtonLabel()
        {
            string selected = recipeButtonLabel?.Invoke();
            return string.IsNullOrEmpty(selected)
                ? Lang.Get("vintagekinematics:jsonprocessor-recipes-button")
                : Lang.Get("vintagekinematics:recipebrowser-selected", selected);
        }

        private List<IRecipeBrowserListItem> BuildRecipeListItems()
        {
            if (buildRecipeItems != null)
            {
                List<IRecipeBrowserListItem> customItems = new List<IRecipeBrowserListItem>();
                IEnumerable<IRecipeBrowserListItem> built = buildRecipeItems();
                if (built == null) return customItems;
                foreach (IRecipeBrowserListItem item in built)
                {
                    if (item != null) customItems.Add(item);
                }
                return customItems;
            }

            var registry = capi.ModLoader.GetModSystem<KineticProcessRecipeRegistry>();
            List<IRecipeBrowserListItem> items = new List<IRecipeBrowserListItem>();

            if (recipeSource == "mixer")
            {
                var mixerRegistry = capi.ModLoader.GetModSystem<KineticMixerRecipeRegistry>();
                if (mixerRegistry == null) return items;

                foreach (KineticMixerRecipe recipe in mixerRegistry.Recipes)
                {
                    items.Add(new MixerRecipeListItem(recipe, capi));
                }
                return items;
            }

            if (registry == null || string.IsNullOrEmpty(machineCode)) return items;

            foreach (KineticProcessRecipe recipe in registry.Recipes)
            {
                if (recipe?.Machine != machineCode) continue;
                items.Add(new ProcessRecipeListItem(recipe));
            }
            return items;
        }

        private static int[] SlotRange(int first, int last)
        {
            int[] slots = new int[last - first + 1];
            for (int i = 0; i < slots.Length; i++) slots[i] = first + i;
            return slots;
        }

        private static double ProgressBarX(double left, double rowWidth, double barWidth, string align)
        {
            return align == "left" ? left : left + (rowWidth - barWidth) / 2.0;
        }

        private static string[] RecipeSortValues()
        {
            return new[] { "output", "input" };
        }

        private static string[] RecipeSortNames()
        {
            return new[]
            {
                Lang.Get("vintagekinematics:recipebrowser-sort-output"),
                Lang.Get("vintagekinematics:recipebrowser-sort-input")
            };
        }

        public override void Dispose()
        {
            recipeBrowser?.Dispose();
            base.Dispose();
        }
    }
}
