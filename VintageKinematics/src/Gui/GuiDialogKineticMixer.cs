using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using VintageKinematics.Crafting;

namespace VintageKinematics.Gui
{
    public class GuiDialogKineticMixer : GuiDialogBlockEntity
    {
        private readonly MachineProgressBar progressBar;
        private readonly MachineRecipeBrowser<IRecipeBrowserListItem> recipeBrowser;
        private const string RecipeButtonKey = "kineticmixer-recipes-button";

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
            recipeBrowser = new MachineRecipeBrowser<IRecipeBrowserListItem>(
                "kineticmixer-recipe",
                "vintagekinematics:kineticmixer-recipes",
                "vintagekinematics:kineticmixer-search-recipes",
                BuildRecipeListItems,
                null,
                () => SingleComposer,
                width: 520.0,
                listHeight: 330.0,
                cellHeight: 76,
                sortValues: RecipeSortValues(),
                sortNames: RecipeSortNames());
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
            ElementBounds recipeButtonBounds = ElementBounds.Fixed(slotPad, outputSlotsBounds.fixedY + outputSlotsBounds.fixedHeight + 12.0, rowWidth, 28.0);
            recipeBrowser.SetBounds(slotPad + rowWidth + 24.0, slotPad + topOffset);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            List<ElementBounds> childBounds = new List<ElementBounds>
            {
                inputLabelBounds,
                inputSlotsBounds,
                progressBounds,
                outputLabelBounds,
                outputSlotsBounds,
                recipeButtonBounds
            };
            recipeBrowser.AddBounds(childBounds);
            bgBounds.WithChildren(childBounds.ToArray());

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.RightMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0.0);

            string inputLabel = Lang.Get("vintagekinematics:kineticmixer-input");
            string outputLabel = Lang.Get("vintagekinematics:kineticmixer-outputs");
            int[] inputSlots = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 };
            int[] outputSlots = new[] { 9, 10, 11, 12, 13, 14, 15, 16, 17 };

            GuiComposer oldComposer = SingleComposer;
            GuiComposer composer = capi.Gui.CreateCompo("kineticmixer-" + BlockEntityPosition, dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(title, CloseIconPressed)
                .BeginChildElements(bgBounds)
                    .AddStaticText(inputLabel, CairoFont.WhiteSmallText(), inputLabelBounds)
                    .AddItemSlotGrid(Inventory, DoSendPacket, 3, inputSlots, inputSlotsBounds, "inputslots");

            SingleComposer = progressBar.AddToComposer(composer, progressBounds)
                    .AddStaticText(outputLabel, CairoFont.WhiteSmallText(), outputLabelBounds)
                    .AddItemSlotGrid(Inventory, DoSendPacket, 3, outputSlots, outputSlotsBounds, "outputslots")
                    .AddSmallButton(Lang.Get("vintagekinematics:jsonprocessor-recipes-button"), OnToggleRecipeBrowser, recipeButtonBounds, EnumButtonStyle.Normal, RecipeButtonKey);

            SingleComposer = recipeBrowser.AddToComposer(SingleComposer)
                .EndChildElements()
                .Compose();
            oldComposer?.Dispose();
            recipeBrowser.AfterCompose(SingleComposer);
            progressBar.Refresh(SingleComposer, true);
        }

        private bool OnToggleRecipeBrowser()
        {
            return recipeBrowser.Toggle(() => ComposeDialog(DialogTitle));
        }

        private List<IRecipeBrowserListItem> BuildRecipeListItems()
        {
            var registry = capi.ModLoader.GetModSystem<KineticMixerRecipeRegistry>();
            List<IRecipeBrowserListItem> items = new List<IRecipeBrowserListItem>();
            if (registry == null) return items;

            foreach (KineticMixerRecipe recipe in registry.Recipes)
            {
                items.Add(new MixerRecipeListItem(recipe, capi));
            }
            return items;
        }

        private static string[] RecipeSortValues()
        {
            return new[] { "output", "input", "work" };
        }

        private static string[] RecipeSortNames()
        {
            return new[]
            {
                Lang.Get("vintagekinematics:recipebrowser-sort-output"),
                Lang.Get("vintagekinematics:recipebrowser-sort-input"),
                Lang.Get("vintagekinematics:recipebrowser-sort-work")
            };
        }

        public override void Dispose()
        {
            recipeBrowser?.Dispose();
            base.Dispose();
        }
    }
}
