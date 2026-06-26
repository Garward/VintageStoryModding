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
    internal class MixerRecipeListItem : IRecipeBrowserListItem
    {
        private readonly KineticMixerRecipe recipe;
        private readonly DummySlot iconSlot;
        private readonly string title;
        private readonly string inputLabel;
        private readonly string outputLabel;
        private readonly string liquidLabel;
        private readonly string[] details;
        private readonly string searchText;
        private LoadedTexture titleTexture;
        private LoadedTexture[] detailTextures = new LoadedTexture[0];
        private ElementBounds scissorBounds;

        public bool Visible => true;
        public KineticMixerRecipe Recipe => recipe;
        public string SortTitle => title ?? "";
        public string SelectionKey => recipe?.Code;
        public string SelectionLabel => SortTitle;

        public MixerRecipeListItem(KineticMixerRecipe recipe, ICoreClientAPI capi)
        {
            this.recipe = recipe;

            JsonItemStack firstOutput = FirstOutput(recipe?.Outputs);
            ItemStack iconStack = StackWithRecipeQuantity(firstOutput) ?? FirstResolvedIngredient(recipe?.Ingredients);
            if (iconStack != null) iconSlot = new DummySlot(iconStack.Clone());

            outputLabel = OutputLabel(firstOutput);
            inputLabel = IngredientListLabel(recipe?.Ingredients);
            liquidLabel = LiquidLabel(recipe, capi);
            title = string.IsNullOrEmpty(outputLabel) ? Lang.Get("vintagekinematics:kineticmixer-recipe") : outputLabel;
            details = BuildDetails(recipe, inputLabel, liquidLabel);
            searchText = (title + "\n" + inputLabel + "\n" + outputLabel + "\n" + liquidLabel + "\n" + RecipeCodes(recipe)).ToLowerInvariant();
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
                case "work": return (recipe?.MixTicks ?? 0).ToString("D8") + "\n" + SortTitle;
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

        private static string[] BuildDetails(KineticMixerRecipe recipe, string inputs, string liquid)
        {
            List<string> lines = new List<string> { Lang.Get("vintagekinematics:recipebrowser-inputs", inputs) };
            if (!string.IsNullOrEmpty(liquid)) lines.Add(Lang.Get("vintagekinematics:recipebrowser-liquid", liquid));
            lines.Add(Lang.Get("vintagekinematics:recipebrowser-mix-ticks", Math.Max(1, recipe?.MixTicks ?? 1)));
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

        private static ItemStack FirstResolvedIngredient(JsonItemStack[] ingredients)
        {
            if (ingredients == null) return null;
            for (int i = 0; i < ingredients.Length; i++)
            {
                if (ingredients[i]?.ResolvedItemstack != null) return ingredients[i].ResolvedItemstack.Clone();
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
            string label = StackLabel(output?.ResolvedItemstack, output?.Code, Lang.Get("vintagekinematics:recipebrowser-matching-output"));
            int quantity = Math.Max(1, output?.StackSize ?? 1);
            return quantity == 1 ? label : quantity + "x " + label;
        }

        private static string IngredientListLabel(JsonItemStack[] ingredients)
        {
            if (ingredients == null || ingredients.Length == 0) return Lang.Get("vintagekinematics:recipebrowser-none");

            List<string> parts = new List<string>();
            for (int i = 0; i < ingredients.Length; i++)
            {
                JsonItemStack ingredient = ingredients[i];
                if (ingredient == null) continue;
                string label = IngredientLabel(ingredient);
                int quantity = Math.Max(1, ingredient.StackSize);
                parts.Add(quantity == 1 ? label : quantity + "x " + label);
            }
            return parts.Count == 0 ? Lang.Get("vintagekinematics:recipebrowser-none") : string.Join(", ", parts);
        }

        private static string IngredientLabel(JsonItemStack ingredient)
        {
            if (ingredient?.ResolvedItemstack != null) return ingredient.ResolvedItemstack.GetName();

            AssetLocation code = ingredient?.Code;
            string exact = KnownWildcardIngredientLabel(code);
            if (!string.IsNullOrEmpty(exact)) return exact;

            return StackLabel(null, code, Lang.Get("vintagekinematics:recipebrowser-matching-input"));
        }

        private static string LiquidLabel(KineticMixerRecipe recipe, ICoreClientAPI capi)
        {
            if (recipe?.LiquidCode == null || recipe.LiquidLitres <= 0f) return null;

            string label = recipe.LiquidCode.ToString();
            if (recipe.LiquidCode.Path?.Contains('*') != true)
            {
                Item item = capi.World.GetItem(recipe.LiquidCode);
                if (item != null && !item.IsMissing)
                {
                    label = item.GetHeldItemName(new ItemStack(item));
                }
            }
            else
            {
                label = Lang.Get("vintagekinematics:recipebrowser-matching-liquid");
            }

            return recipe.LiquidLitres.ToString("0.##") + " L " + label;
        }

        private static string StackLabel(ItemStack stack, AssetLocation code, string wildcardFallback)
        {
            if (stack != null) return stack.GetName();
            string fallback = code?.ToString();
            if (fallback != null && fallback.Contains('*'))
            {
                string readable = ReadableWildcardLabel(code);
                return string.IsNullOrEmpty(readable) ? wildcardFallback : readable;
            }
            return string.IsNullOrEmpty(fallback) ? "unknown" : fallback;
        }

        private static string KnownWildcardIngredientLabel(AssetLocation code)
        {
            if (code == null) return null;

            string full = code.ToString();
            switch (full)
            {
                case "vintagekinematics:*-grit": return Lang.Get("vintagekinematics:recipebrowser-rock-grit");
                case "vintagekinematics:*-dust": return Lang.Get("vintagekinematics:recipebrowser-rock-dust");
                case "game:sand-*": return Lang.Get("vintagekinematics:recipebrowser-sand");
                default: return null;
            }
        }

        private static string ReadableWildcardLabel(AssetLocation code)
        {
            string path = code?.Path;
            if (string.IsNullOrEmpty(path)) return null;

            string cleaned = path.Replace("*", "").Trim('-');
            if (string.IsNullOrEmpty(cleaned)) return null;

            string[] parts = cleaned.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return null;

            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (part.Length == 0) continue;
                parts[i] = char.ToUpperInvariant(part[0]) + (part.Length > 1 ? part.Substring(1) : "");
            }
            return string.Join(" ", parts);
        }

        private static string RecipeCodes(KineticMixerRecipe recipe)
        {
            if (recipe == null) return "";

            List<string> parts = new List<string>();
            if (recipe.Ingredients != null)
            {
                for (int i = 0; i < recipe.Ingredients.Length; i++) parts.Add(recipe.Ingredients[i]?.Code?.ToString());
            }
            if (recipe.Outputs != null)
            {
                for (int i = 0; i < recipe.Outputs.Length; i++) parts.Add(recipe.Outputs[i]?.Code?.ToString());
            }
            parts.Add(recipe.LiquidCode?.ToString());
            return string.Join("\n", parts);
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
