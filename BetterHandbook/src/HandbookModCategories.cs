using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace HandbookCache
{
    internal static class HandbookModCategories
    {
        public const string ModsRootCategoryCode = "betterhandbook:mods";
        private const string ModCategoryPrefix = "betterhandbook:mod:";
        private static readonly ConditionalWeakTable<GuiDialogHandbook, ModDomainSelection> SelectedModDomainByDialog = new ConditionalWeakTable<GuiDialogHandbook, ModDomainSelection>();

        private static readonly HashSet<string> HiddenDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "game"
        };

        public static string CategoryCodeForDomain(string domain)
        {
            return ModCategoryPrefix + domain;
        }

        public static bool PageMatchesCategory(GuiHandbookPage page, string categoryCode)
        {
            if (categoryCode == null) return true;
            if (HandbookBookmarks.IsBookmarksCategory(categoryCode)) return HandbookBookmarks.IsBookmarked(page);
            if (IsModsRootCategory(categoryCode)) return false;

            if (TryGetModDomain(categoryCode, out string domain))
            {
                return TryGetPageDomain(page, out string pageDomain)
                    && string.Equals(pageDomain, domain, StringComparison.OrdinalIgnoreCase);
            }

            return page.CategoryCode == categoryCode;
        }

        public static bool IsModsRootCategory(string categoryCode)
        {
            return string.Equals(categoryCode, ModsRootCategoryCode, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsModDomainCategory(string categoryCode)
        {
            return TryGetModDomain(categoryCode, out _);
        }

        public static bool IsManagedCategory(string categoryCode)
        {
            return IsModsRootCategory(categoryCode) || HandbookBookmarks.IsBookmarksCategory(categoryCode);
        }

        public static string EffectiveCategoryCode(GuiDialogHandbook dialog, string categoryCode)
        {
            if (IsModDomainCategory(categoryCode))
            {
                SelectModDomain(dialog, categoryCode);
                return categoryCode;
            }

            if ((categoryCode == null || IsModsRootCategory(categoryCode)) && TryGetSelectedModDomain(dialog, out string selectedCategoryCode))
            {
                return selectedCategoryCode;
            }

            return categoryCode;
        }

        public static void SelectModDomain(GuiDialogHandbook dialog, string categoryCode)
        {
            if (dialog == null || !IsModDomainCategory(categoryCode)) return;
            SelectedModDomainByDialog.GetOrCreateValue(dialog).CategoryCode = categoryCode;
        }

        public static void ClearSelectedModDomain(GuiDialogHandbook dialog)
        {
            if (dialog == null) return;
            SelectedModDomainByDialog.Remove(dialog);
        }

        public static bool TryGetSelectedModDomain(GuiDialogHandbook dialog, out string categoryCode)
        {
            categoryCode = null;
            if (dialog == null || !SelectedModDomainByDialog.TryGetValue(dialog, out ModDomainSelection selection))
            {
                return false;
            }

            categoryCode = selection.CategoryCode;
            return IsModDomainCategory(categoryCode);
        }

        public static void ResetModDomainToRoot(GuiDialogHandbook dialog)
        {
            if (dialog != null && IsModDomainCategory(dialog.currentCatgoryCode))
            {
                ClearSelectedModDomain(dialog);
                dialog.currentCatgoryCode = ModsRootCategoryCode;
            }
        }

        public static GuiTab[] AppendModTabs(ICoreClientAPI capi, GuiTab[] originalTabs, IList<GuiHandbookPage> pages, string currentCategoryCode, ref int curTab)
        {
            if (capi == null || originalTabs == null)
            {
                return originalTabs;
            }

            List<GuiTab> tabs = new List<GuiTab>(originalTabs);
            int tabIndex = Math.Min(2, tabs.Count);
            tabs.Insert(tabIndex, new HandbookTab
            {
                PaddingTop = 20.0,
                DataInt = tabIndex,
                Name = "Mods",
                CategoryCode = ModsRootCategoryCode
            });

            if (IsModsRootCategory(currentCategoryCode) || IsModDomainCategory(currentCategoryCode))
            {
                curTab = tabIndex;
            }
            else if (curTab >= tabIndex)
            {
                curTab++;
            }

            HandbookBookmarks.AppendBookmarkTab(capi, tabs, currentCategoryCode, ref curTab);
            return tabs.ToArray();
        }

        public static List<IFlatListItem> BuildModListItems(ICoreClientAPI capi, IList<GuiHandbookPage> pages, string searchText)
        {
            string normalizedSearch = (searchText ?? "").Trim();
            return BuildDomainSummaries(capi, pages)
                .Where(summary => normalizedSearch.Length == 0 || MatchesSearch(summary, normalizedSearch))
                .Select(summary => (IFlatListItem)new ModCategoryListItem(
                    summary.Domain,
                    summary.DisplayName,
                    summary.Count,
                    CategoryCodeForDomain(summary.Domain)))
                .ToList();
        }

        private static List<ModDomainSummary> BuildDomainSummaries(ICoreClientAPI capi, IList<GuiHandbookPage> pages)
        {
            Dictionary<string, int> countsByDomain = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < pages.Count; i++)
            {
                GuiHandbookPage page = pages[i];
                if (page == null || page.IsDuplicate) continue;
                if (!TryGetPageDomain(page, out string domain)) continue;
                if (HiddenDomains.Contains(domain)) continue;

                countsByDomain.TryGetValue(domain, out int count);
                countsByDomain[domain] = count + 1;
            }

            return countsByDomain
                .Select(pair => new ModDomainSummary(pair.Key, ModDisplayName(capi, pair.Key), pair.Value))
                .OrderBy(summary => summary.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(summary => summary.Domain, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string ModDisplayName(ICoreClientAPI capi, string domain)
        {
            try
            {
                Mod mod = ((ICoreAPI)capi).ModLoader.GetMod(domain);
                string name = mod?.Info?.Name;
                return string.IsNullOrWhiteSpace(name) ? domain : name;
            }
            catch
            {
                return domain;
            }
        }

        private static bool TryGetModDomain(string categoryCode, out string domain)
        {
            domain = null;
            if (categoryCode == null || !categoryCode.StartsWith(ModCategoryPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            domain = categoryCode.Substring(ModCategoryPrefix.Length);
            return !string.IsNullOrWhiteSpace(domain);
        }

        private static bool MatchesSearch(ModDomainSummary summary, string searchText)
        {
            return summary.DisplayName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0
                || summary.Domain.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryGetPageDomain(GuiHandbookPage page, out string domain)
        {
            domain = null;

            if (page is GuiHandbookGroupedItemstackPage groupedPage)
            {
                for (int i = 0; i < groupedPage.Stacks.Count; i++)
                {
                    if (TryGetStackDomain(groupedPage.Stacks[i], out domain))
                    {
                        return true;
                    }
                }
            }

            if (page is GuiHandbookItemStackPage itemStackPage && TryGetStackDomain(itemStackPage.Stack, out domain))
            {
                return true;
            }

            return TryGetDomainFromPageCode(page.PageCode, out domain);
        }

        private static bool TryGetStackDomain(ItemStack stack, out string domain)
        {
            domain = stack?.Collectible?.Code?.Domain;
            return !string.IsNullOrWhiteSpace(domain);
        }

        private static bool TryGetDomainFromPageCode(string pageCode, out string domain)
        {
            domain = null;
            if (string.IsNullOrWhiteSpace(pageCode)) return false;

            int separatorIndex = pageCode.IndexOf(':');
            if (separatorIndex <= 0) return false;

            domain = pageCode.Substring(0, separatorIndex);
            return !string.IsNullOrWhiteSpace(domain);
        }

        private readonly struct ModDomainSummary
        {
            public readonly string Domain;
            public readonly string DisplayName;
            public readonly int Count;

            public ModDomainSummary(string domain, string displayName, int count)
            {
                Domain = domain;
                DisplayName = displayName;
                Count = count;
            }
        }

        private sealed class ModDomainSelection
        {
            public string CategoryCode;
        }
    }

    internal sealed class ModCategoryListItem : IFlatListItem
    {
        private readonly string domain;
        private readonly string displayName;
        private readonly int count;
        private LoadedTexture texture;

        public string CategoryCode { get; }

        public bool Visible => true;

        public ModCategoryListItem(string domain, string displayName, int count, string categoryCode)
        {
            this.domain = domain;
            this.displayName = displayName;
            this.count = count;
            CategoryCode = categoryCode;
        }

        public void RenderListEntryTo(ICoreClientAPI capi, float dt, double x, double y, double cellWidth, double cellHeight)
        {
            if (texture == null)
            {
                string label = string.Format("{0} ({1})", displayName, count);
                texture = new TextTextureUtil(capi).GenTextTexture(label, CairoFont.WhiteSmallText(), null);
            }

            double xOffset = GuiElement.scaled(10.0);
            double yOffset = GuiElement.scaled(6.0);
            capi.Render.Render2DTexturePremultipliedAlpha(texture.TextureId, x + xOffset, y + yOffset, texture.Width, texture.Height, 50f, (Vec4f)null);
        }

        public void Dispose()
        {
            texture?.Dispose();
            texture = null;
        }
    }

    [HarmonyPatch(typeof(GuiDialogSurvivalHandbook), "genTabs")]
    internal static class HandbookModTabsPatch
    {
        private static readonly AccessTools.FieldRef<GuiDialogHandbook, List<GuiHandbookPage>> AllPages =
            AccessTools.FieldRefAccess<GuiDialogHandbook, List<GuiHandbookPage>>("allHandbookPages");

        private static readonly AccessTools.FieldRef<GuiDialog, ICoreClientAPI> ClientApi =
            AccessTools.FieldRefAccess<GuiDialog, ICoreClientAPI>("capi");

        public static void Postfix(GuiDialogSurvivalHandbook __instance, ref GuiTab[] __result, ref int curTab)
        {
            try
            {
                __result = HandbookModCategories.AppendModTabs(
                    ClientApi(__instance),
                    __result,
                    AllPages(__instance),
                    __instance.currentCatgoryCode,
                    ref curTab);
            }
            catch (Exception ex)
            {
                HandbookCacheDiagnostics.LogFailure(ClientApi(__instance), "Failed to append mod handbook tabs: {0}", ex);
            }
        }
    }

    [HarmonyPatch(typeof(GuiDialogHandbook), "onLeftClickListElement")]
    internal static class HandbookModListClickPatch
    {
        private static readonly AccessTools.FieldRef<GuiDialogHandbook, List<IFlatListItem>> ShownPages =
            AccessTools.FieldRefAccess<GuiDialogHandbook, List<IFlatListItem>>("shownHandbookPages");

        private static readonly AccessTools.FieldRef<GuiDialogHandbook, string> CurrentSearchText =
            AccessTools.FieldRefAccess<GuiDialogHandbook, string>("currentSearchText");

        private static readonly AccessTools.FieldRef<GuiDialogHandbook, GuiComposer> OverviewGui =
            AccessTools.FieldRefAccess<GuiDialogHandbook, GuiComposer>("overviewGui");

        private static readonly AccessTools.FieldRef<GuiDialog, ICoreClientAPI> ClientApi =
            AccessTools.FieldRefAccess<GuiDialog, ICoreClientAPI>("capi");

        public static bool Prefix(GuiDialogHandbook __instance, int index)
        {
            try
            {
                List<IFlatListItem> shownPages = ShownPages(__instance);
                if (index < 0 || index >= shownPages.Count) return true;
                if (!(shownPages[index] is ModCategoryListItem modItem)) return true;

                __instance.currentCatgoryCode = modItem.CategoryCode;
                HandbookModCategories.SelectModDomain(__instance, modItem.CategoryCode);
                CurrentSearchText(__instance) = "";
                OverviewGui(__instance)?.GetTextInput("searchField")?.SetValue("", true);
                ClientApi(__instance).Settings.String["currentHandbookCategoryCode"] = HandbookModCategories.ModsRootCategoryCode;
                HandbookFilterCachePatch.ApplyFilter(__instance);
                return false;
            }
            catch (Exception ex)
            {
                HandbookCacheDiagnostics.LogFailure(ClientApi(__instance), "Failed to open mod handbook category: {0}", ex);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(GuiDialogHandbook), "OnTabClicked")]
    internal static class HandbookModsTabClickPatch
    {
        private static readonly AccessTools.FieldRef<GuiDialogHandbook, string> CurrentSearchText =
            AccessTools.FieldRefAccess<GuiDialogHandbook, string>("currentSearchText");

        private static readonly AccessTools.FieldRef<GuiDialogHandbook, GuiComposer> OverviewGui =
            AccessTools.FieldRefAccess<GuiDialogHandbook, GuiComposer>("overviewGui");

        private static readonly AccessTools.FieldRef<GuiDialog, ICoreClientAPI> ClientApi =
            AccessTools.FieldRefAccess<GuiDialog, ICoreClientAPI>("capi");

        [HarmonyPriority(Priority.Last)]
        public static void Postfix(GuiDialogHandbook __instance, GuiTab tab)
        {
            if (!(tab is HandbookTab handbookTab) || !HandbookModCategories.IsManagedCategory(handbookTab.CategoryCode))
            {
                HandbookModCategories.ClearSelectedModDomain(__instance);
                return;
            }

            SelectManagedTab(__instance, handbookTab.CategoryCode);
        }

        private static void SelectManagedTab(GuiDialogHandbook dialog, string categoryCode)
        {
            HandbookModCategories.ClearSelectedModDomain(dialog);
            dialog.currentCatgoryCode = categoryCode;
            CurrentSearchText(dialog) = "";
            OverviewGui(dialog)?.GetTextInput("searchField")?.SetValue("", true);
            ClientApi(dialog).Settings.String["currentHandbookCategoryCode"] = categoryCode;
            HandbookFilterCachePatch.ApplyFilter(dialog);
        }
    }

    [HarmonyPatch(typeof(GuiDialogHandbook), "initOverviewGui")]
    internal static class HandbookModOverviewCategoryPatch
    {
        private static readonly AccessTools.FieldRef<GuiDialogHandbook, string> CurrentSearchText =
            AccessTools.FieldRefAccess<GuiDialogHandbook, string>("currentSearchText");

        private static readonly AccessTools.FieldRef<GuiDialogHandbook, GuiComposer> OverviewGui =
            AccessTools.FieldRefAccess<GuiDialogHandbook, GuiComposer>("overviewGui");

        public static void Prefix(GuiDialogHandbook __instance, out string __state)
        {
            __state = __instance.currentCatgoryCode;
        }

        public static void Postfix(GuiDialogHandbook __instance, string __state)
        {
            string categoryCode = __state;
            if (HandbookModCategories.IsModDomainCategory(categoryCode))
            {
                HandbookModCategories.SelectModDomain(__instance, categoryCode);
            }
            else if (!HandbookModCategories.TryGetSelectedModDomain(__instance, out categoryCode))
            {
                return;
            }

            // Vanilla can only select the visible "Mods" tab, so it rewrites hidden
            // per-mod categories back to the mods root during overview recomposition.
            __instance.currentCatgoryCode = categoryCode;

            string searchText = CurrentSearchText(__instance);
            if (!string.IsNullOrEmpty(searchText))
            {
                OverviewGui(__instance)?.GetTextInput("searchField")?.SetValue(searchText, false);
            }

            HandbookFilterCachePatch.ApplyFilter(__instance);
        }
    }
}
