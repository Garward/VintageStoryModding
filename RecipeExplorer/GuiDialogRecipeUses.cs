using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace RecipeExplorer
{
    /// <summary>
    /// GUI dialog showing all recipes that use a specific item
    /// </summary>
    public class GuiDialogRecipeUses : GuiDialog
    {
        private ItemStack itemStack;
        private List<RecipeInfo> recipes;

        public override string ToggleKeyCombinationCode => null;

        public GuiDialogRecipeUses(ICoreClientAPI capi, ItemStack stack, List<RecipeInfo> recipeList) : base(capi)
        {
            capi.Logger.Debug("[RecipeExplorer] GuiDialogRecipeUses constructor called");
            itemStack = stack;
            recipes = recipeList ?? new List<RecipeInfo>();
            capi.Logger.Debug("[RecipeExplorer] Constructor complete, recipe count: {0}", recipes.Count);
        }

        public override void OnGuiOpened()
        {
            capi.Logger.Debug("[RecipeExplorer] OnGuiOpened called");
            base.OnGuiOpened();
            capi.Logger.Debug("[RecipeExplorer] About to call SetupDialog");
            SetupDialog();
            capi.Logger.Debug("[RecipeExplorer] SetupDialog complete");
        }

        private void SetupDialog()
        {
            capi.Logger.Debug("[RecipeExplorer] SetupDialog start");
            string itemName = itemStack?.GetName() ?? "Unknown Item";

            // Dialog bounds
            double dialogWidth = 600;
            double dialogHeight = 550;
            double listHeight = 400;

            ElementBounds textBounds = ElementBounds.Fixed(9, 45, dialogWidth - 30, listHeight);
            ElementBounds clipBounds = textBounds.ForkBoundingParent();
            ElementBounds insetBounds = textBounds.FlatCopy().FixedGrow(6).WithFixedOffset(-3, -3);
            ElementBounds scrollbarBounds = insetBounds.CopyOffsetedSibling(textBounds.fixedWidth + 7).WithFixedWidth(20);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(insetBounds, clipBounds, scrollbarBounds);

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            // Build richtext with clickable links (limit to reasonable number to avoid crashes)
            int maxDisplay = Math.Min(50, recipes.Count);
            var components = new List<RichTextComponentBase>();

            for (int i = 0; i < maxDisplay; i++)
            {
                var recipe = recipes[i];
                if (recipe?.Name == null) continue;

                // Bullet point
                components.Add(new RichTextComponent(capi, "  • ", CairoFont.WhiteSmallText()));

                // Clickable recipe name
                string pageCode = recipe.GetHandbookPageCode();
                if (!string.IsNullOrEmpty(pageCode))
                {
                    string href = "handbook://" + pageCode;
                    if (i == 0)
                    {
                        capi.Logger.Debug("[RecipeExplorer] First recipe href: {0}", href);
                    }
                    var link = new LinkTextComponent(capi, recipe.Name, CairoFont.WhiteSmallText(),
                        (linkComp) => {
                            // Invoke handbook link protocol
                            capi.Logger.Debug("[RecipeExplorer] Link clicked: {0}", href);
                            linkComp.Href = href;
                            linkComp.HandleLink();
                        });
                    components.Add(link);
                }
                else
                {
                    components.Add(new RichTextComponent(capi, recipe.Name, CairoFont.WhiteSmallText()));
                }

                components.Add(new RichTextComponent(capi, "\n", CairoFont.WhiteSmallText()));
            }

            if (recipes.Count > maxDisplay)
            {
                components.Add(new RichTextComponent(capi, $"\n... and {recipes.Count - maxDisplay} more\n", CairoFont.WhiteSmallText()));
            }

            capi.Logger.Debug("[RecipeExplorer] Creating composer with {0} components", components.Count);
            SingleComposer = capi.Gui.CreateCompo("recipeuses-" + itemStack?.Collectible?.Code, dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar($"Recipes using {itemName}", OnTitleBarClose)
                .BeginChildElements(bgBounds)
                    .BeginClip(clipBounds)
                        .AddInset(insetBounds, 3)
                        .AddRichtext(components.ToArray(), textBounds, "recipelist")
                    .EndClip()
                    .AddVerticalScrollbar(OnNewScrollbarValue, scrollbarBounds, "scrollbar")
                .EndChildElements()
                .Compose();

            // Set up scrollbar
            var richtext = SingleComposer.GetRichtext("recipelist");
            SingleComposer.GetScrollbar("scrollbar").SetHeights(
                (float)listHeight,
                (float)richtext.Bounds.fixedHeight
            );

            capi.Logger.Debug("[RecipeExplorer] Setup complete");
        }

        private void OnNewScrollbarValue(float value)
        {
            var richtext = SingleComposer.GetRichtext("recipelist");
            richtext.Bounds.fixedY = 45 - value;
            richtext.Bounds.CalcWorldBounds();
        }

        private void OnTitleBarClose()
        {
            TryClose();
        }

        private bool OnCloseButton()
        {
            TryClose();
            return true;
        }

        public override bool TryClose()
        {
            return base.TryClose();
        }
    }
}
