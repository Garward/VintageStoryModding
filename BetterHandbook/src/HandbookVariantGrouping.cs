using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace HandbookCache
{
    internal static class HandbookVariantGrouping
    {
        private static readonly Regex TrailingParenthetical = new Regex(@"\s+\([^)]{1,80}\)\s*$", RegexOptions.Compiled);
        private static readonly ConditionalWeakTable<GuiDialogHandbook, Cache> CacheByDialog = new ConditionalWeakTable<GuiDialogHandbook, Cache>();
        private static readonly HashSet<string> CollapsibleVariantKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "rock",
            "wood",
            "metal",
            "material"
        };

        internal static List<GuiHandbookPage> GetDisplayPages(GuiDialogHandbook dialog, ICoreClientAPI capi, List<GuiHandbookPage> allPages)
        {
            if (allPages == null || !RecipeExplorer.BetterHandbookFeatures.VariantGrouping)
            {
                return allPages;
            }

            Cache cache = CacheByDialog.GetOrCreateValue(dialog);
            if (cache.SourceCount == allPages.Count && cache.Pages != null)
            {
                return cache.Pages;
            }

            cache.SourceCount = allPages.Count;
            cache.Pages = BuildDisplayPages(capi, allPages);
            return cache.Pages;
        }

        internal static void Clear(GuiDialogHandbook dialog)
        {
            if (dialog == null) return;
            CacheByDialog.Remove(dialog);
        }

        private static List<GuiHandbookPage> BuildDisplayPages(ICoreClientAPI capi, List<GuiHandbookPage> allPages)
        {
            var output = new List<GuiHandbookPage>(allPages.Count);
            var groups = new Dictionary<string, Group>();
            var entries = new List<Entry>(allPages.Count);

            for (int i = 0; i < allPages.Count; i++)
            {
                GuiHandbookPage page = allPages[i];
                if (!TryGetCandidateInfo(page, out string key))
                {
                    entries.Add(new Entry(page, null));
                    continue;
                }

                if (!groups.TryGetValue(key, out Group group))
                {
                    group = new Group();
                    groups[key] = group;
                }

                group.Pages.Add((GuiHandbookItemStackPage)page);
                entries.Add(new Entry(page, group));
            }

            var emitted = new HashSet<Group>();
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (entry.Group == null)
                {
                    output.Add(entry.Page);
                    continue;
                }

                if (!emitted.Add(entry.Group)) continue;

                if (entry.Group.Pages.Count <= 1 || !TryGetDisplayName(entry.Group.Pages[0], out string displayName))
                {
                    for (int pageIndex = 0; pageIndex < entry.Group.Pages.Count; pageIndex++)
                    {
                        output.Add(entry.Group.Pages[pageIndex]);
                    }
                    continue;
                }

                output.Add(new VariantGroupHandbookPage(capi, displayName, entry.Group.Pages));
            }

            return output;
        }

        private static bool TryGetCandidateInfo(GuiHandbookPage page, out string key)
        {
            key = null;

            if (!(page is GuiHandbookItemStackPage itemPage)) return false;
            CollectibleObject collectible = itemPage.Stack?.Collectible;
            if (collectible?.Code == null || collectible.Variant == null) return false;

            if (!TryGetVariant(collectible, out string variantKey, out string variantValue)) return false;
            string basePath = BasePathWithoutVariant(collectible.Code.Path, variantValue);
            if (string.IsNullOrWhiteSpace(basePath)) return false;
            if (string.Equals(basePath, collectible.Code.Path, StringComparison.OrdinalIgnoreCase)) return false;

            string domain = collectible.Code.Domain ?? "";
            key = domain + "|" + itemPage.CategoryCode + "|" + variantKey.ToLowerInvariant() + "|" + basePath.ToLowerInvariant();
            return true;
        }

        private static bool TryGetVariant(CollectibleObject collectible, out string variantKey, out string variantValue)
        {
            variantKey = null;
            variantValue = null;

            foreach (string key in CollapsibleVariantKeys)
            {
                string value = collectible.Variant[key];
                if (string.IsNullOrWhiteSpace(value)) continue;

                variantKey = key;
                variantValue = value;
                return true;
            }

            return false;
        }

        private static string BasePathWithoutVariant(string path, string variantValue)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(variantValue))
            {
                return path;
            }

            string[] parts = path.Split('-');
            var kept = new List<string>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.Equals(parts[i], variantValue, StringComparison.OrdinalIgnoreCase)) continue;
                kept.Add(parts[i]);
            }

            return kept.Count == parts.Length ? path : string.Join("-", kept);
        }

        private static bool TryGetDisplayName(GuiHandbookItemStackPage page, out string displayName)
        {
            displayName = null;

            string name = page.Stack?.GetName();
            if (string.IsNullOrWhiteSpace(name)) return false;

            string collapsed = TrailingParenthetical.Replace(name, "");
            if (string.Equals(collapsed, name, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(collapsed))
            {
                return false;
            }

            displayName = collapsed.Trim();
            return true;
        }

        private sealed class Cache
        {
            public int SourceCount = -1;
            public List<GuiHandbookPage> Pages;
        }

        private sealed class Group
        {
            public readonly List<GuiHandbookItemStackPage> Pages = new List<GuiHandbookItemStackPage>();
        }

        private readonly struct Entry
        {
            public readonly GuiHandbookPage Page;
            public readonly Group Group;

            public Entry(GuiHandbookPage page, Group group)
            {
                Page = page;
                Group = group;
            }
        }
    }

    internal sealed class VariantGroupHandbookPage : GuiHandbookItemStackPage
    {
        private readonly string displayName;
        private readonly List<GuiHandbookItemStackPage> pages;
        private readonly string searchText;

        public override string PageCode => "betterhandbook-variantgroup-" + Stack.Collectible.Code.Domain + "-" + displayName.ToSearchFriendly();

        public VariantGroupHandbookPage(ICoreClientAPI capi, string displayName, List<GuiHandbookItemStackPage> pages)
            : base(capi, pages[0].Stack)
        {
            this.displayName = displayName;
            this.pages = pages;
            searchText = BuildSearchText(displayName, pages);
        }

        public override void RenderListEntryTo(ICoreClientAPI capi, float dt, double x, double y, double cellWidth, double cellHeight)
        {
            float size = (float)GuiElement.scaled(25.0);
            float pad = (float)GuiElement.scaled(10.0);
            int index = pages.Count == 0 ? 0 : (int)(capi.ElapsedMilliseconds / 1000 % pages.Count);

            dummySlot.Itemstack = pages[index].Stack;
            capi.Render.RenderItemstackToGui(dummySlot, x + pad + size / 2f, y + size / 2f, 100.0, size, -1, shading: true, rotate: false, showStackSize: false);

            if (Texture == null)
            {
                Texture = new TextTextureUtil(capi).GenTextTexture(displayName, CairoFont.WhiteSmallText());
            }

            capi.Render.Render2DTexturePremultipliedAlpha(Texture.TextureId, x + size + GuiElement.scaled(25.0), y + size / 4f - GuiElement.scaled(3.0), Texture.Width, Texture.Height);
        }

        public override void ComposePage(GuiComposer detailViewGui, ElementBounds textBounds, ItemStack[] allstacks, ActionConsumable<string> openDetailPageFor)
        {
            pages[0].ComposePage(detailViewGui, textBounds, allstacks, openDetailPageFor);
        }

        public override PageText GetPageText()
        {
            return new PageText
            {
                Title = displayName.ToSearchFriendly(),
                Text = searchText
            };
        }

        private static string BuildSearchText(string displayName, List<GuiHandbookItemStackPage> pages)
        {
            var builder = new StringBuilder();
            builder.Append(displayName.ToSearchFriendly());

            for (int i = 0; i < pages.Count; i++)
            {
                ItemStack stack = pages[i].Stack;
                if (stack == null) continue;

                if (stack.Collectible?.Code != null)
                {
                    builder.Append(' ');
                    builder.Append(stack.Collectible.Code);
                    builder.Append(' ');
                    builder.Append(stack.Collectible.Code.ToShortString());
                }
            }

            return builder.ToString();
        }
    }
}
