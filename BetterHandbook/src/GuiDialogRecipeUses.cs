using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace RecipeExplorer
{
    public class GuiDialogRecipeUses : GuiDialog
    {
        private enum Mode { Ingredient, Tool, Produces, MachineOutputs }

        private ItemStack itemStack;
        private List<RecipeInfo> ingredientRecipes;
        private List<RecipeInfo> toolRecipes;
        private List<RecipeInfo> producesRecipes;
        private List<RecipeInfo> machineRecipes;
        private Mode mode;
        private int currentPage;

        private const string ToggleButtonKey = "modeToggle";
        private const string PrevButtonKey = "prevPage";
        private const string NextButtonKey = "nextPage";
        private const double DialogWidth = 600;
        private const double ListHeight = 400;
        private const int PageSize = 50;

        public override string ToggleKeyCombinationCode => null;

        public GuiDialogRecipeUses(ICoreClientAPI capi, ItemStack stack, List<RecipeInfo> ingredients, List<RecipeInfo> tools, List<RecipeInfo> produces, List<RecipeInfo> machineOutputs) : base(capi)
        {
            itemStack = stack;
            ingredientRecipes = ingredients ?? new List<RecipeInfo>();
            toolRecipes = tools ?? new List<RecipeInfo>();
            producesRecipes = produces ?? new List<RecipeInfo>();
            machineRecipes = machineOutputs ?? new List<RecipeInfo>();
            ingredientRecipes.Sort((a, b) => string.Compare(a?.Name, b?.Name, StringComparison.OrdinalIgnoreCase));
            toolRecipes.Sort((a, b) => string.Compare(a?.Name, b?.Name, StringComparison.OrdinalIgnoreCase));
            producesRecipes.Sort((a, b) => string.Compare(a?.Name, b?.Name, StringComparison.OrdinalIgnoreCase));
            machineRecipes.Sort((a, b) => string.Compare(a?.Name, b?.Name, StringComparison.OrdinalIgnoreCase));

            mode = PickInitialMode();
        }

        private Mode PickInitialMode()
        {
            // Machine takes top priority — if you pressed U on the crusher, that's what you wanted.
            if (machineRecipes.Count > 0) return Mode.MachineOutputs;
            if (ingredientRecipes.Count > 0) return Mode.Ingredient;
            if (producesRecipes.Count > 0) return Mode.Produces;
            if (toolRecipes.Count > 0) return Mode.Tool;
            return Mode.Ingredient;
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            ComposeDialog();
        }

        private List<RecipeInfo> Active
        {
            get
            {
                switch (mode)
                {
                    case Mode.Tool: return toolRecipes;
                    case Mode.Produces: return producesRecipes;
                    case Mode.MachineOutputs: return machineRecipes;
                    default: return ingredientRecipes;
                }
            }
        }

        private int PageCount => Math.Max(1, (Active.Count + PageSize - 1) / PageSize);

        // Cycle in fixed display order, skipping any mode whose list is empty.
        private static readonly Mode[] CycleOrder = { Mode.Ingredient, Mode.Tool, Mode.Produces, Mode.MachineOutputs };

        private Mode NextMode(Mode m)
        {
            int start = Array.IndexOf(CycleOrder, m);
            if (start < 0) start = 0;
            for (int step = 1; step <= CycleOrder.Length; step++)
            {
                var candidate = CycleOrder[(start + step) % CycleOrder.Length];
                if (ModeCount(candidate) > 0) return candidate;
            }
            return m;
        }

        private bool HasOtherNonEmptyMode()
        {
            foreach (var c in CycleOrder)
            {
                if (c != mode && ModeCount(c) > 0) return true;
            }
            return false;
        }

        private string ModeName(Mode m)
        {
            switch (m)
            {
                case Mode.Tool: return "tool uses";
                case Mode.Produces: return "produced by";
                case Mode.MachineOutputs: return "made by this machine";
                default: return "ingredient uses";
            }
        }

        private int ModeCount(Mode m)
        {
            switch (m)
            {
                case Mode.Tool: return toolRecipes.Count;
                case Mode.Produces: return producesRecipes.Count;
                case Mode.MachineOutputs: return machineRecipes.Count;
                default: return ingredientRecipes.Count;
            }
        }

        private string ToggleLabel()
        {
            var nxt = NextMode(mode);
            return string.Format("Show {0} ({1})", ModeName(nxt), ModeCount(nxt));
        }

        private string TitleForMode()
        {
            string itemName = itemStack?.GetName() ?? "Unknown Item";
            switch (mode)
            {
                case Mode.Tool: return string.Format("Recipes using {0} as tool", itemName);
                case Mode.Produces: return string.Format("Recipes that produce {0}", itemName);
                case Mode.MachineOutputs: return string.Format("Recipes the {0} can produce", itemName);
                default: return string.Format("Recipes using {0}", itemName);
            }
        }

        private void ComposeDialog()
        {
            string title = TitleForMode();

            currentPage = Math.Min(currentPage, PageCount - 1);
            if (currentPage < 0) currentPage = 0;

            ElementBounds textBounds = ElementBounds.Fixed(9, 45, DialogWidth - 30, ListHeight);
            ElementBounds clipBounds = textBounds.ForkBoundingParent();
            ElementBounds insetBounds = textBounds.FlatCopy().FixedGrow(6).WithFixedOffset(-3, -3);
            ElementBounds scrollbarBounds = insetBounds.CopyOffsetedSibling(textBounds.fixedWidth + 7).WithFixedWidth(20);

            double navY = 45 + ListHeight + 16;
            double navBtnW = 80;
            ElementBounds prevBounds = ElementBounds.Fixed(9, navY, navBtnW, 28);
            ElementBounds pageLabelBounds = ElementBounds.Fixed(9 + navBtnW + 8, navY + 4, DialogWidth - 30 - 2 * (navBtnW + 8), 22);
            ElementBounds nextBounds = ElementBounds.Fixed(9 + DialogWidth - 30 - navBtnW, navY, navBtnW, 28);
            ElementBounds toggleBounds = ElementBounds.Fixed(9, navY + 36, DialogWidth - 30, 28);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(insetBounds, clipBounds, scrollbarBounds, prevBounds, pageLabelBounds, nextBounds, toggleBounds);

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            int startIdx = currentPage * PageSize;
            int endIdx = Math.Min(startIdx + PageSize, Active.Count);
            var components = new List<RichTextComponentBase>();

            for (int i = startIdx; i < endIdx; i++)
            {
                var recipe = Active[i];
                if (recipe?.Name == null) continue;

                components.Add(new RichTextComponent(capi, "  • ", CairoFont.WhiteSmallText()));

                string pageCode = recipe.GetHandbookPageCode();
                if (!string.IsNullOrEmpty(pageCode))
                {
                    string href = "handbook://" + pageCode;
                    var link = new LinkTextComponent(capi, recipe.Name, CairoFont.WhiteSmallText(),
                        (linkComp) => { linkComp.Href = href; linkComp.HandleLink(); });
                    components.Add(link);
                }
                else
                {
                    components.Add(new RichTextComponent(capi, recipe.Name, CairoFont.WhiteSmallText()));
                }

                components.Add(new RichTextComponent(capi, "\n", CairoFont.WhiteSmallText()));
            }

            if (Active.Count == 0)
            {
                components.Add(new RichTextComponent(capi, "  (no recipes)\n", CairoFont.WhiteSmallText()));
            }

            string pageLabel = Active.Count == 0
                ? ""
                : string.Format("Page {0} of {1}  ({2}-{3} of {4})",
                    currentPage + 1, PageCount, startIdx + 1, endIdx, Active.Count);
            var pageLabelFont = CairoFont.WhiteSmallText();
            pageLabelFont.Orientation = EnumTextOrientation.Center;

            SingleComposer = capi.Gui.CreateCompo("recipeuses-" + itemStack?.Collectible?.Code, dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(title, OnTitleBarClose)
                .BeginChildElements(bgBounds)
                    .BeginClip(clipBounds)
                        .AddInset(insetBounds, 3)
                        .AddRichtext(components.ToArray(), textBounds, "recipelist")
                    .EndClip()
                    .AddVerticalScrollbar(OnNewScrollbarValue, scrollbarBounds, "scrollbar")
                    .AddSmallButton("< Prev", OnPrevPage, prevBounds, EnumButtonStyle.Normal, PrevButtonKey)
                    .AddStaticText(pageLabel, pageLabelFont, pageLabelBounds, "pageLabel")
                    .AddSmallButton("Next >", OnNextPage, nextBounds, EnumButtonStyle.Normal, NextButtonKey)
                    .AddSmallButton(ToggleLabel(), OnToggleMode, toggleBounds, EnumButtonStyle.Normal, ToggleButtonKey)
                .EndChildElements()
                .Compose();

            var richtext = SingleComposer.GetRichtext("recipelist");
            SingleComposer.GetScrollbar("scrollbar").SetHeights(
                (float)ListHeight,
                (float)richtext.Bounds.fixedHeight
            );

            SingleComposer.GetButton(PrevButtonKey).Enabled = currentPage > 0;
            SingleComposer.GetButton(NextButtonKey).Enabled = currentPage < PageCount - 1;
            SingleComposer.GetButton(ToggleButtonKey).Enabled = HasOtherNonEmptyMode();
        }

        private bool OnToggleMode()
        {
            mode = NextMode(mode);
            currentPage = 0;
            ComposeDialog();
            return true;
        }

        private bool OnPrevPage()
        {
            if (currentPage > 0)
            {
                currentPage--;
                ComposeDialog();
            }
            return true;
        }

        private bool OnNextPage()
        {
            if (currentPage < PageCount - 1)
            {
                currentPage++;
                ComposeDialog();
            }
            return true;
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

        public override bool TryClose()
        {
            return base.TryClose();
        }
    }
}
