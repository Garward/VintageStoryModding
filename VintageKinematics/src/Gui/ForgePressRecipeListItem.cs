using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VintageKinematics.Crafting;

namespace VintageKinematics.Gui
{
    internal class ForgePressRecipeListItem : IFlatListItem
    {
        private readonly KineticForgePressRecipe recipe;
        private readonly DummySlot iconSlot;
        private readonly string title;
        private readonly string[] details;
        private readonly string searchText;
        private LoadedTexture titleTexture;
        private LoadedTexture[] detailTextures = new LoadedTexture[0];
        private ElementBounds scissorBounds;

        public bool Visible => true;
        public KineticForgePressRecipe Recipe => recipe;

        public ForgePressRecipeListItem(KineticForgePressRecipe recipe)
        {
            this.recipe = recipe;
            ItemStack iconStack = FirstResolvedStack(recipe?.Outputs) ?? recipe?.Ingredient?.ResolvedItemstack;
            if (iconStack != null) iconSlot = new DummySlot(iconStack.Clone());

            string outputLabel = StackLabel(FirstResolvedStack(recipe?.Outputs), FirstCode(recipe?.Outputs));
            if (IsWildcardOutput(recipe) && !string.IsNullOrEmpty(recipe?.DisplayName))
            {
                outputLabel = recipe.DisplayName;
            }

            string variantLabel = VariantLabel(recipe);
            title = string.IsNullOrEmpty(outputLabel) ? recipe?.DisplayName ?? "Recipe" : outputLabel;

            var detailList = new List<string>
            {
                string.Format("{0}  |  Input: {1}  |  {2:0} C", recipe?.DisplayName ?? "", IngredientLabel(recipe?.Ingredient), recipe?.RequiredTemperature ?? 0f)
            };
            if (recipe?.Die?.Code != null)
            {
                detailList.Add("Die: " + StackLabel(recipe.Die.ResolvedItemstack, recipe.Die.Code.ToString()));
            }
            if (!string.IsNullOrEmpty(variantLabel))
            {
                detailList.Add("Metals: " + variantLabel);
            }
            details = detailList.ToArray();
            searchText = (title + "\n" + string.Join("\n", details) + "\n" + recipe?.OperationCode + "\n" + recipe?.Ingredient?.Code + "\n" + recipe?.Die?.Code + "\n" + FirstCode(recipe?.Outputs) + "\n" + variantLabel).ToLowerInvariant();
        }

        public bool Matches(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            return searchText.IndexOf(text.Trim().ToLowerInvariant(), StringComparison.Ordinal) >= 0;
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

        private static ItemStack FirstResolvedStack(JsonItemStack[] stacks)
        {
            if (stacks == null) return null;
            for (int i = 0; i < stacks.Length; i++)
            {
                if (stacks[i]?.ResolvedItemstack != null) return stacks[i].ResolvedItemstack;
            }
            return null;
        }

        private static string FirstCode(JsonItemStack[] stacks)
        {
            if (stacks == null) return null;
            for (int i = 0; i < stacks.Length; i++)
            {
                if (stacks[i]?.Code != null) return stacks[i].Code.ToString();
            }
            return null;
        }

        private static string StackLabel(ItemStack stack, string fallback)
        {
            if (stack != null) return stack.GetName();
            return string.IsNullOrEmpty(fallback) ? "unknown" : fallback;
        }

        private static string IngredientLabel(JsonItemStack ingredient)
        {
            string label = StackLabel(ingredient?.ResolvedItemstack, ingredient?.Code?.ToString());
            int quantity = Math.Max(1, ingredient?.StackSize ?? 1);
            return quantity == 1 ? label : quantity + "x " + label;
        }

        private static bool IsWildcardOutput(KineticForgePressRecipe recipe)
        {
            if (recipe?.Outputs == null) return false;
            for (int i = 0; i < recipe.Outputs.Length; i++)
            {
                if (recipe.Outputs[i]?.Code?.Path?.Contains('*') == true) return true;
            }
            return false;
        }

        private static string VariantLabel(KineticForgePressRecipe recipe)
        {
            if (recipe?.AllowedVariants == null || recipe.AllowedVariants.Length == 0) return "";
            string[] labels = new string[recipe.AllowedVariants.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                labels[i] = PrettyVariant(recipe.AllowedVariants[i]);
            }
            return string.Join(", ", labels);
        }

        private static string PrettyVariant(string variant)
        {
            switch (variant)
            {
                case "tinbronze": return "tin bronze";
                case "bismuthbronze": return "bismuth bronze";
                case "blackbronze": return "black bronze";
                case "meteoriciron": return "meteoric iron";
                default: return variant ?? "";
            }
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
