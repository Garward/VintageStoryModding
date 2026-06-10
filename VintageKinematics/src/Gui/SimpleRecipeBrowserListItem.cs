using System;
using System.Collections.Generic;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VintageKinematics.Gui
{
    /// <summary>
    /// General-purpose recipe-browser row for addon machines. Use this when a recipe can be
    /// represented as an icon, a title, a few detail lines, and optional sort/search text.
    /// </summary>
    public class SimpleRecipeBrowserListItem : IRecipeBrowserListItem
    {
        private readonly DummySlot iconSlot;
        private readonly string title;
        private readonly string[] details;
        private readonly string searchText;
        private readonly Dictionary<string, string> sortKeys;
        private LoadedTexture titleTexture;
        private LoadedTexture[] detailTextures = new LoadedTexture[0];
        private ElementBounds scissorBounds;

        public bool Visible => true;
        public string SortTitle => title ?? "";

        public SimpleRecipeBrowserListItem(
            string title,
            ItemStack iconStack = null,
            IEnumerable<string> details = null,
            string searchText = null,
            IDictionary<string, string> sortKeys = null)
        {
            this.title = string.IsNullOrEmpty(title) ? "Recipe" : title;
            if (iconStack != null) iconSlot = new DummySlot(iconStack.Clone());

            List<string> detailList = new List<string>();
            if (details != null)
            {
                foreach (string detail in details)
                {
                    if (!string.IsNullOrEmpty(detail)) detailList.Add(detail);
                }
            }
            this.details = detailList.ToArray();

            this.searchText = (this.title + "\n" + string.Join("\n", this.details) + "\n" + (searchText ?? "")).ToLowerInvariant();
            this.sortKeys = sortKeys == null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(sortKeys, StringComparer.OrdinalIgnoreCase);
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
            if (!string.IsNullOrEmpty(sortMode) && sortKeys.TryGetValue(sortMode, out string key)) return key ?? "";
            return SortTitle;
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
