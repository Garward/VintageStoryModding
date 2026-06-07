using System;
using System.Collections.Generic;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using VintageKinematics.Crafting;

namespace VintageKinematics.Gui
{
    internal class ProcessRecipeListItem : IRecipeBrowserListItem
    {
        private readonly KineticProcessRecipe recipe;
        private readonly DummySlot iconSlot;
        private readonly string title;
        private readonly string inputLabel;
        private readonly string outputLabel;
        private readonly string[] details;
        private readonly string searchText;
        private LoadedTexture titleTexture;
        private LoadedTexture[] detailTextures = new LoadedTexture[0];
        private ElementBounds scissorBounds;

        public bool Visible => true;
        public string SortTitle => title ?? "";

        public ProcessRecipeListItem(KineticProcessRecipe recipe)
        {
            this.recipe = recipe;
            JsonItemStack firstOutput = FirstOutput(recipe?.Outputs);
            ItemStack iconStack = StackWithRecipeQuantity(firstOutput) ?? recipe?.Ingredient?.ResolvedItemstack;
            if (iconStack != null) iconSlot = new DummySlot(iconStack.Clone());

            outputLabel = OutputLabel(firstOutput);
            inputLabel = IngredientLabel(recipe?.Ingredient);
            title = string.IsNullOrEmpty(outputLabel) ? "Recipe" : outputLabel;
            details = BuildDetails(recipe, inputLabel);
            searchText = (title + "\n" + inputLabel + "\n" + outputLabel + "\n" + recipe?.Machine + "\n" + recipe?.Ingredient?.Code + "\n" + firstOutput?.Code).ToLowerInvariant();
        }

        public int SearchScore(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            string trimmed = text.Trim();
            string needle = trimmed.ToLowerInvariant();
            if (string.IsNullOrEmpty(needle)) return 0;

            if (string.Equals(title, trimmed, StringComparison.OrdinalIgnoreCase)) return 1;
            if (title?.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase) == true) return 5;
            if (searchText.IndexOf(needle, StringComparison.Ordinal) >= 0) return 20;
            return int.MaxValue;
        }

        public string SortKey(string sortMode)
        {
            switch (sortMode)
            {
                case "input": return inputLabel ?? "";
                default: return outputLabel ?? SortTitle;
            }
        }

        public void RenderListEntryTo(ICoreClientAPI capi, float dt, double x, double y, double cellWidth, double cellHeight)
        {
            EnsureTextures(capi);

            double iconSize = GuiElement.scaled(34.0);
            double iconPad = GuiElement.scaled(8.0);
            double iconClipSize = GuiElement.scaled(64.0);
            if (iconSlot != null)
            {
                scissorBounds.fixedX = (x + iconPad) / RuntimeEnv.GUIScale;
                scissorBounds.fixedY = (y - (iconClipSize - iconSize) / 2.0) / RuntimeEnv.GUIScale;
                scissorBounds.fixedWidth = iconClipSize / RuntimeEnv.GUIScale;
                scissorBounds.fixedHeight = iconClipSize / RuntimeEnv.GUIScale;
                scissorBounds.CalcWorldBounds();
                capi.Render.PushScissor(scissorBounds, true);
                capi.Render.RenderItemstackToGui(iconSlot, x + iconPad + iconSize / 2.0, y + iconSize / 2.0, 100.0, (float)iconSize, -1, true, false, false);
                capi.Render.PopScissor();
            }

            double textX = x + GuiElement.scaled(70.0);
            capi.Render.Render2DTexturePremultipliedAlpha(titleTexture.TextureId, textX, y + GuiElement.scaled(2.0), titleTexture.Width, titleTexture.Height, 50f, (Vec4f)null);
            for (int i = 0; i < detailTextures.Length; i++)
            {
                LoadedTexture texture = detailTextures[i];
                if (texture == null) continue;
                capi.Render.Render2DTexturePremultipliedAlpha(texture.TextureId, textX, y + GuiElement.scaled(20.0 + i * 13.0), texture.Width, texture.Height, 50f, (Vec4f)null);
            }
        }

        private void EnsureTextures(ICoreClientAPI capi)
        {
            if (titleTexture != null && detailTextures.Length == details.Length) return;

            TextTextureUtil util = new TextTextureUtil(capi);
            titleTexture = util.GenTextTexture(title, CairoFont.WhiteSmallText(), null);
            CairoFont detailFont = CairoFont.WhiteDetailText();
            detailFont.Color[3] *= 0.8;
            detailTextures = new LoadedTexture[details.Length];
            for (int i = 0; i < details.Length; i++)
            {
                detailTextures[i] = util.GenTextTexture(details[i], detailFont, null);
            }
            scissorBounds = ElementBounds.FixedSize(64.0, 64.0);
            scissorBounds.ParentBounds = capi.Gui.WindowBounds;
        }

        private static string[] BuildDetails(KineticProcessRecipe recipe, string input)
        {
            List<string> lines = new List<string> { "Input: " + input };
            if (!string.IsNullOrEmpty(recipe?.Machine)) lines.Add("Machine: " + recipe.Machine);
            return lines.ToArray();
        }

        private static JsonItemStack FirstOutput(JsonItemStack[] stacks)
        {
            if (stacks == null) return null;
            for (int i = 0; i < stacks.Length; i++)
            {
                if (stacks[i] != null) return stacks[i];
            }
            return null;
        }

        private static ItemStack StackWithRecipeQuantity(JsonItemStack stack)
        {
            if (stack?.ResolvedItemstack == null) return null;

            ItemStack clone = stack.ResolvedItemstack.Clone();
            clone.StackSize = Math.Max(1, stack.StackSize);
            return clone;
        }

        private static string OutputLabel(JsonItemStack output)
        {
            string label = StackLabel(output?.ResolvedItemstack, output?.Code?.ToString());
            int quantity = Math.Max(1, output?.StackSize ?? 1);
            return quantity == 1 ? label : quantity + "x " + label;
        }

        private static string IngredientLabel(JsonItemStack ingredient)
        {
            string label = StackLabel(ingredient?.ResolvedItemstack, ingredient?.Code?.ToString());
            int quantity = Math.Max(1, ingredient?.StackSize ?? 1);
            return quantity == 1 ? label : quantity + "x " + label;
        }

        private static string StackLabel(ItemStack stack, string fallback)
        {
            if (stack != null) return stack.GetName();
            if (fallback != null && fallback.Contains('*')) return Lang.Get("vintagekinematics:recipebrowser-matching-output");
            return string.IsNullOrEmpty(fallback) ? "unknown" : fallback;
        }

        public void Dispose()
        {
            titleTexture?.Dispose();
            if (detailTextures != null)
            {
                for (int i = 0; i < detailTextures.Length; i++)
                {
                    detailTextures[i]?.Dispose();
                }
            }
            titleTexture = null;
            detailTextures = new LoadedTexture[0];
        }
    }
}
