using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace HandbookCache
{
    internal static class HandbookCacheDiagnostics
    {
        public static bool Enabled = false;

        public static void Log(ICoreClientAPI capi, string message, params object[] args)
        {
            if (capi == null) return;
            if (Enabled || RecipeExplorer.BetterHandbookLog.Config?.EnableDiagnosticLogging == true)
            {
                capi.Logger.Notification("[BetterHandbook] " + message, args);
            }
        }

        public static void LogFailure(ICoreClientAPI capi, string message, params object[] args)
        {
            if (capi == null) return;
            RecipeExplorer.BetterHandbookLog.Failure(capi, "[BetterHandbook] " + message, args);
        }
    }

    [HarmonyPatch(typeof(GuiDialogHandbook), nameof(GuiDialogHandbook.FilterItems))]
    internal static class HandbookFilterCachePatch
    {
        private const int InitialResultBatchSize = 750;
        private const int AdditionalResultBatchSize = 750;

        private static readonly ConditionalWeakTable<GuiDialogHandbook, CacheState> CacheByDialog = new ConditionalWeakTable<GuiDialogHandbook, CacheState>();

        private static readonly AccessTools.FieldRef<GuiDialogHandbook, List<IFlatListItem>> ShownPages =
            AccessTools.FieldRefAccess<GuiDialogHandbook, List<IFlatListItem>>("shownHandbookPages");

        private static readonly AccessTools.FieldRef<GuiDialogHandbook, List<GuiHandbookPage>> AllPages =
            AccessTools.FieldRefAccess<GuiDialogHandbook, List<GuiHandbookPage>>("allHandbookPages");

        private static readonly AccessTools.FieldRef<GuiDialogHandbook, string> CurrentSearchText =
            AccessTools.FieldRefAccess<GuiDialogHandbook, string>("currentSearchText");

        private static readonly AccessTools.FieldRef<GuiDialogHandbook, bool> LoadingPagesAsync =
            AccessTools.FieldRefAccess<GuiDialogHandbook, bool>("loadingPagesAsync");

        private static readonly AccessTools.FieldRef<GuiDialogHandbook, GuiComposer> OverviewGui =
            AccessTools.FieldRefAccess<GuiDialogHandbook, GuiComposer>("overviewGui");

        private static readonly AccessTools.FieldRef<GuiDialogHandbook, double> ListHeight =
            AccessTools.FieldRefAccess<GuiDialogHandbook, double>("listHeight");

        private static readonly AccessTools.FieldRef<GuiDialog, ICoreClientAPI> ClientApi =
            AccessTools.FieldRefAccess<GuiDialog, ICoreClientAPI>("capi");

        public static bool Prefix(GuiDialogHandbook __instance)
        {
            ApplyFilter(__instance);
            return false;
        }

        internal static void ApplyFilter(GuiDialogHandbook dialog)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            List<IFlatListItem> shownPages = ShownPages(dialog);
            List<GuiHandbookPage> allPages = AllPages(dialog);

            if (LoadingPagesAsync(dialog) || allPages == null)
            {
                UpdateScrollbar(dialog);
                HandbookCacheDiagnostics.Log(ClientApi(dialog), "Filter skipped loading={0} allPagesNull={1} elapsed={2}ms", LoadingPagesAsync(dialog), allPages == null, stopwatch.ElapsedMilliseconds);
                return;
            }

            string categoryCode = dialog.currentCatgoryCode;
            string effectiveCategoryCode = HandbookModCategories.EffectiveCategoryCode(dialog, categoryCode);
            string searchText = CurrentSearchText(dialog) ?? "";
            string cacheKey = MakeCacheKey(effectiveCategoryCode, searchText);

            CacheState state = CacheByDialog.GetOrCreateValue(dialog);
            if (state.PageCount != allPages.Count)
            {
                state.Clear(allPages.Count);
            }

            if (HandbookModCategories.IsModsRootCategory(effectiveCategoryCode))
            {
                state.Clear(allPages.Count);
                DisposeGeneratedRows(shownPages);
                shownPages.Clear();
                shownPages.AddRange(HandbookModCategories.BuildModListItems(ClientApi(dialog), allPages, searchText));
                UpdateScrollbar(dialog);
                HandbookCacheDiagnostics.Log(
                    ClientApi(dialog),
                    "Filter mod list shown={0} search='{1}' elapsed={2}ms",
                    shownPages.Count,
                    searchText,
                    stopwatch.ElapsedMilliseconds);
                return;
            }

            if (searchText.Length == 0)
            {
                if (state.ActiveKey != cacheKey || !state.ActiveIsLazyEmpty)
                {
                    state.StartLazyEmpty(cacheKey, effectiveCategoryCode);
                }

                EnsureLazyEmptyLoaded(state, allPages, InitialResultBatchSize);
                DisposeGeneratedRows(shownPages);
                shownPages.Clear();
                AddLoadedResults(shownPages, state.LazyEmptyResults, state.LoadedCount);
                UpdateScrollbar(dialog);
                HandbookCacheDiagnostics.Log(
                    ClientApi(dialog),
                    "Filter empty lazy category={0} shown={1} scanned={2}/{3} scanElapsed={4}ms elapsed={5}ms",
                    effectiveCategoryCode ?? "<all>",
                    state.LoadedCount,
                    state.NextPageIndex,
                    allPages.Count,
                    state.LastLazyScanElapsedMs,
                    stopwatch.ElapsedMilliseconds);
                return;
            }

            bool cacheHit = state.ResultsByKey.ContainsKey(cacheKey);
            if (!state.ResultsByKey.TryGetValue(cacheKey, out List<IFlatListItem> cachedResults))
            {
                try
                {
                    cachedResults = BuildResults(allPages, effectiveCategoryCode, searchText);
                }
                catch (Exception ex)
                {
                    HandbookCacheDiagnostics.LogFailure(
                        ClientApi(dialog),
                        "Filter search failed category={0} text='{1}'; keeping previous results: {2}",
                        effectiveCategoryCode ?? "<all>",
                        searchText,
                        ex);
                    UpdateScrollbar(dialog);
                    return;
                }
                state.ResultsByKey[cacheKey] = cachedResults;
            }

            if (state.ActiveKey != cacheKey)
            {
                state.ActiveKey = cacheKey;
                state.ActiveCategoryCode = effectiveCategoryCode;
                state.ActiveIsLazyEmpty = false;
                state.LoadedCount = Math.Min(InitialResultBatchSize, cachedResults.Count);
            }

            DisposeGeneratedRows(shownPages);
            shownPages.Clear();
            AddLoadedResults(shownPages, cachedResults, state.LoadedCount);
            UpdateScrollbar(dialog);
            HandbookCacheDiagnostics.Log(
                ClientApi(dialog),
                "Filter search hit={0} text='{1}' category={2} shown={3}/{4} pages={5} elapsed={6}ms",
                cacheHit,
                searchText,
                effectiveCategoryCode ?? "<all>",
                state.LoadedCount,
                cachedResults.Count,
                allPages.Count,
                stopwatch.ElapsedMilliseconds);
            return;
        }

        internal static void Clear(GuiDialogHandbook dialog)
        {
            if (dialog == null) return;
            CacheByDialog.Remove(dialog);
        }

        internal static void LoadMoreIfNeeded(GuiDialogHandbook dialog, float scrollValue)
        {
            try
            {
                if (dialog == null) return;
                if (!CacheByDialog.TryGetValue(dialog, out CacheState state)) return;
                if (state.ActiveKey == null || state.AppendQueued) return;

                GuiComposer overviewGui = OverviewGui(dialog);
                if (overviewGui == null) return;

                GuiElementFlatList stacklist = overviewGui.GetFlatList("stacklist");
                double remainingScroll = stacklist.insideBounds.fixedHeight - ListHeight(dialog) - scrollValue;
                if (remainingScroll > ListHeight(dialog)) return;

                state.AppendQueued = true;
                ICoreClientAPI capi = ClientApi(dialog);
                capi.Event.EnqueueMainThreadTask(() => AppendMoreResults(dialog), "handbookcache-lazy-append");
                HandbookCacheDiagnostics.Log(capi, "Lazy append queued scroll={0:0.##} remaining={1:0.##}", scrollValue, remainingScroll);
            }
            catch (Exception ex)
            {
                HandbookCacheDiagnostics.LogFailure(dialog == null ? null : ClientApi(dialog), "Lazy append queue failed: {0}", ex);
            }
        }

        private static void AppendMoreResults(GuiDialogHandbook dialog)
        {
            if (dialog == null) return;
            Stopwatch stopwatch = Stopwatch.StartNew();
            ICoreClientAPI capi = ClientApi(dialog);

            try
            {
                if (!dialog.IsOpened()) return;
                if (!CacheByDialog.TryGetValue(dialog, out CacheState state)) return;
                state.AppendQueued = false;
                if (state.ActiveKey == null) return;

                List<IFlatListItem> shownPages = ShownPages(dialog);

                if (state.ActiveIsLazyEmpty)
                {
                    List<GuiHandbookPage> allPages = AllPages(dialog);
                    int oldCount = state.LoadedCount;
                    EnsureLazyEmptyLoaded(state, allPages, state.LoadedCount + AdditionalResultBatchSize);
                    for (int i = oldCount; i < state.LoadedCount; i++)
                    {
                        shownPages.Add(state.LazyEmptyResults[i]);
                    }

                    UpdateScrollbar(dialog);
                    HandbookCacheDiagnostics.Log(
                        capi,
                        "Lazy append empty shown={0} scanned={1}/{2} scanElapsed={3}ms elapsed={4}ms",
                        state.LoadedCount,
                        state.NextPageIndex,
                        allPages.Count,
                        state.LastLazyScanElapsedMs,
                        stopwatch.ElapsedMilliseconds);
                    return;
                }

                if (!state.ResultsByKey.TryGetValue(state.ActiveKey, out List<IFlatListItem> cachedResults)) return;
                if (state.LoadedCount >= cachedResults.Count) return;

                int oldCachedCount = state.LoadedCount;
                state.LoadedCount = Math.Min(state.LoadedCount + AdditionalResultBatchSize, cachedResults.Count);

                for (int i = oldCachedCount; i < state.LoadedCount; i++)
                {
                    shownPages.Add(cachedResults[i]);
                }

                UpdateScrollbar(dialog);
                HandbookCacheDiagnostics.Log(
                    capi,
                    "Lazy append search shown={0}/{1} elapsed={2}ms",
                    state.LoadedCount,
                    cachedResults.Count,
                    stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                HandbookCacheDiagnostics.LogFailure(capi, "Lazy append failed: {0}", ex);
            }
            finally
            {
                if (dialog != null && CacheByDialog.TryGetValue(dialog, out CacheState state))
                {
                    state.AppendQueued = false;
                }
            }
        }

        private static void EnsureLazyEmptyLoaded(CacheState state, List<GuiHandbookPage> allPages, int targetCount)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            int oldIndex = state.NextPageIndex;
            while (state.LazyEmptyResults.Count < targetCount && state.NextPageIndex < allPages.Count)
            {
                GuiHandbookPage page = allPages[state.NextPageIndex++];
                if (!HandbookModCategories.PageMatchesCategory(page, state.ActiveCategoryCode)) continue;
                if (page.IsDuplicate) continue;

                state.LazyEmptyResults.Add(page);
            }

            state.LoadedCount = state.LazyEmptyResults.Count;
            state.LastLazyScanElapsedMs = stopwatch.ElapsedMilliseconds;
            state.LastLazyScannedPages = state.NextPageIndex - oldIndex;
        }

        private static void AddLoadedResults(List<IFlatListItem> shownPages, List<IFlatListItem> cachedResults, int loadedCount)
        {
            for (int i = 0; i < loadedCount; i++)
            {
                shownPages.Add(cachedResults[i]);
            }
        }

        private static void DisposeGeneratedRows(List<IFlatListItem> shownPages)
        {
            for (int i = 0; i < shownPages.Count; i++)
            {
                if (shownPages[i] is ModCategoryListItem)
                {
                    shownPages[i].Dispose();
                }
            }
        }

        private static List<IFlatListItem> BuildResults(List<GuiHandbookPage> allPages, string categoryCode, string searchText)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            List<WeightedPage> foundPages = new List<WeightedPage>();
            Regex regex = GuiDialogHandbook.RegexFromSearchText(searchText);
            Regex strictRegex = GuiDialogHandbook.RegexFromSearchText(searchText, true);

            for (int i = 0; i < allPages.Count; i++)
            {
                GuiHandbookPage page = allPages[i];
                if (!HandbookModCategories.PageMatchesCategory(page, categoryCode)) continue;
                if (page.IsDuplicate) continue;

                PageText pageText = page.GetPageText();
                int titleMatches = CountMatches(pageText.Title ?? "", regex);
                int strictTitleMatches = CountMatches(pageText.Title ?? "", strictRegex);
                int textMatches = CountMatches(pageText.Text ?? "", regex);
                int extraMatches = CountMatches(ExtraSearchText(page, categoryCode), regex);

                if (titleMatches > 0 || textMatches > 0 || extraMatches > 0)
                {
                    foundPages.Add(new WeightedPage(new WeightedHandbookPage
                    {
                        Page = page,
                        TitleMatches = titleMatches,
                        StrictTitleMatches = strictTitleMatches,
                        TitleLength = pageText.Title?.Length ?? 0,
                        TextMatches = textMatches + extraMatches,
                        SearchWeight = 1f + page.SearchWeightOffset
                    }));
                }
            }

            foundPages.Sort(CompareWeightedPages);

            return foundPages
                .Select(page => (IFlatListItem)page.Value.Page)
                .ToList();
        }

        private static string ExtraSearchText(GuiHandbookPage page, string categoryCode)
        {
            if (!HandbookModCategories.IsModDomainCategory(categoryCode))
            {
                return "";
            }

            if (page is GuiHandbookGroupedItemstackPage groupedPage)
            {
                return string.Join(" ", groupedPage.Stacks.Select(StackSearchText))
                    + " "
                    + (groupedPage.Name ?? "")
                    + " "
                    + (groupedPage.PageCode ?? "");
            }

            if (page is GuiHandbookItemStackPage itemStackPage)
            {
                return StackSearchText(itemStackPage.Stack) + " " + (itemStackPage.PageCode ?? "");
            }

            return page.PageCode ?? "";
        }

        private static string StackSearchText(ItemStack stack)
        {
            if (stack?.Collectible?.Code == null)
            {
                return "";
            }

            return stack.Collectible.Code.ToString() + " " + stack.Collectible.Code.ToShortString();
        }

        private static int CountMatches(string text, Regex regex)
        {
            return regex.Matches(text).Count;
        }

        private static int CompareWeightedPages(WeightedPage wrappedA, WeightedPage wrappedB)
        {
            WeightedHandbookPage a = wrappedA.Value;
            WeightedHandbookPage b = wrappedB.Value;

            int strictTitleCompare = b.StrictTitleMatches - a.StrictTitleMatches;
            if (strictTitleCompare != 0)
            {
                return strictTitleCompare;
            }

            int titleCompare = b.TitleMatches - a.TitleMatches;
            if (titleCompare != 0)
            {
                return titleCompare;
            }

            int weightCompare = b.SearchWeight.CompareTo(a.SearchWeight);
            if (weightCompare != 0)
            {
                return weightCompare;
            }

            int titleLengthCompare = a.TitleLength - b.TitleLength;
            return titleLengthCompare != 0 ? titleLengthCompare : b.TextMatches - a.TextMatches;
        }

        private static string MakeCacheKey(string categoryCode, string searchText)
        {
            string category = categoryCode ?? "";
            string bookmarkVersion = HandbookBookmarks.IsBookmarksCategory(categoryCode)
                ? "\nbookmarks:" + HandbookBookmarks.Version.ToString()
                : "";

            return category + "\n" + searchText + bookmarkVersion;
        }

        private static void UpdateScrollbar(GuiDialogHandbook dialog)
        {
            GuiComposer overviewGui = OverviewGui(dialog);
            if (overviewGui == null) return;

            GuiElementFlatList stacklist = overviewGui.GetFlatList("stacklist");
            stacklist.CalcTotalHeight();
            overviewGui.GetScrollbar("scrollbar").SetHeights((float)ListHeight(dialog), (float)stacklist.insideBounds.fixedHeight);
        }

        private sealed class CacheState
        {
            public int PageCount = -1;
            public string ActiveKey;
            public string ActiveCategoryCode;
            public bool ActiveIsLazyEmpty;
            public bool AppendQueued;
            public int NextPageIndex;
            public int LoadedCount;
            public long LastLazyScanElapsedMs;
            public int LastLazyScannedPages;
            public readonly List<IFlatListItem> LazyEmptyResults = new List<IFlatListItem>();
            public readonly Dictionary<string, List<IFlatListItem>> ResultsByKey = new Dictionary<string, List<IFlatListItem>>();

            public void Clear(int pageCount)
            {
                PageCount = pageCount;
                ActiveKey = null;
                ActiveCategoryCode = null;
                ActiveIsLazyEmpty = false;
                AppendQueued = false;
                NextPageIndex = 0;
                LoadedCount = 0;
                LastLazyScanElapsedMs = 0;
                LastLazyScannedPages = 0;
                LazyEmptyResults.Clear();
                ResultsByKey.Clear();
            }

            public void StartLazyEmpty(string activeKey, string categoryCode)
            {
                ActiveKey = activeKey;
                ActiveCategoryCode = categoryCode;
                ActiveIsLazyEmpty = true;
                AppendQueued = false;
                NextPageIndex = 0;
                LoadedCount = 0;
                LastLazyScanElapsedMs = 0;
                LastLazyScannedPages = 0;
                LazyEmptyResults.Clear();
            }
        }

        private readonly struct WeightedPage
        {
            public readonly WeightedHandbookPage Value;

            public WeightedPage(WeightedHandbookPage value)
            {
                Value = value;
            }
        }
    }

    [HarmonyPatch(typeof(GuiDialogHandbook), "FilterItemsBySearchText")]
    internal static class HandbookSearchTextPatch
    {
        private static readonly AccessTools.FieldRef<GuiDialogHandbook, string> CurrentSearchText =
            AccessTools.FieldRefAccess<GuiDialogHandbook, string>("currentSearchText");

        private static readonly AccessTools.FieldRef<GuiDialog, ICoreClientAPI> ClientApi =
            AccessTools.FieldRefAccess<GuiDialog, ICoreClientAPI>("capi");

        public static bool Prefix(GuiDialogHandbook __instance, string text)
        {
            string oldText = CurrentSearchText(__instance);
            if (oldText == text)
            {
                return false;
            }

            CurrentSearchText(__instance) = text;
            HandbookFilterCachePatch.ApplyFilter(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(GuiDialogHandbook), "OnNewScrollbarvalueOverviewPage")]
    internal static class HandbookOverviewScrollPatch
    {
        public static void Postfix(GuiDialogHandbook __instance, float value)
        {
            HandbookFilterCachePatch.LoadMoreIfNeeded(__instance, value);
        }
    }

    [HarmonyPatch(typeof(GuiDialogHandbook), nameof(GuiDialogHandbook.OnGuiOpened))]
    internal static class HandbookOpenCachePatch
    {
        private static readonly ConditionalWeakTable<GuiDialogHandbook, WarmedState> WarmedByDialog = new ConditionalWeakTable<GuiDialogHandbook, WarmedState>();

        private static readonly AccessTools.FieldRef<GuiDialogHandbook, GuiComposer> OverviewGui =
            AccessTools.FieldRefAccess<GuiDialogHandbook, GuiComposer>("overviewGui");

        private static readonly AccessTools.FieldRef<GuiDialogHandbook, Stack<BrowseHistoryElement>> BrowseHistory =
            AccessTools.FieldRefAccess<GuiDialogHandbook, Stack<BrowseHistoryElement>>("browseHistory");

        private static readonly AccessTools.FieldRef<GuiDialogHandbook, bool> LoadingPagesAsync =
            AccessTools.FieldRefAccess<GuiDialogHandbook, bool>("loadingPagesAsync");

        private static readonly AccessTools.FieldRef<GuiDialog, ICoreClientAPI> ClientApi =
            AccessTools.FieldRefAccess<GuiDialog, ICoreClientAPI>("capi");

        public static bool Prefix(GuiDialogHandbook __instance)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            if (LoadingPagesAsync(__instance))
            {
                HandbookCacheDiagnostics.Log(ClientApi(__instance), "Open using vanilla path because pages are still loading");
                return true;
            }

            HandbookModCategories.ResetModDomainToRoot(__instance);

            if (!WarmedByDialog.GetOrCreateValue(__instance).Ready)
            {
                HandbookCacheDiagnostics.Log(ClientApi(__instance), "Open using vanilla path because overview is not warmed");
                return true;
            }

            GuiComposer overviewGui = OverviewGui(__instance);
            if (overviewGui == null)
            {
                HandbookCacheDiagnostics.Log(ClientApi(__instance), "Open using vanilla path because overviewGui is null");
                return true;
            }

            ICoreClientAPI capi = ClientApi(__instance);
            HandbookFilterCachePatch.ApplyFilter(__instance);
            if (!TryOpenLockedPage(__instance, capi))
            {
                FocusSearchField(overviewGui);
            }

            if (capi.IsSinglePlayer && !capi.OpenedToLan && !capi.Settings.Bool["noHandbookPause"])
            {
                capi.PauseGame(true);
            }

            HandbookCacheDiagnostics.Log(capi, "Open reused warmed overview elapsed={0}ms", stopwatch.ElapsedMilliseconds);
            return false;
        }

        public static void Postfix(GuiDialogHandbook __instance)
        {
            if (LoadingPagesAsync(__instance)) return;
            if (OverviewGui(__instance) == null) return;

            TryOpenLockedPage(__instance, ClientApi(__instance));
            WarmedByDialog.GetOrCreateValue(__instance).Ready = true;
            HandbookCacheDiagnostics.Log(ClientApi(__instance), "Open marked overview warmed");
        }

        internal static void Clear(GuiDialogHandbook dialog)
        {
            if (dialog == null) return;
            WarmedByDialog.Remove(dialog);
        }

        private static void FocusSearchField(GuiComposer overviewGui)
        {
            GuiElementTextInput searchField = overviewGui?.GetTextInput("searchField");
            if (searchField == null) return;

            overviewGui.FocusElement(searchField.TabIndex);
        }

        private static bool TryOpenLockedPage(GuiDialogHandbook dialog, ICoreClientAPI capi)
        {
            if (dialog == null || capi == null) return false;
            if (BrowseHistory(dialog)?.Count > 0) return false;

            string pageCode = HandbookBookmarks.LockedPageCode(capi);
            if (string.IsNullOrEmpty(pageCode)) return false;

            return dialog.OpenDetailPageFor(pageCode);
        }

        private sealed class WarmedState
        {
            public bool Ready;
        }
    }

    [HarmonyPatch(typeof(GuiDialogHandbook), "loadEntries")]
    internal static class HandbookLoadEntriesCacheInvalidationPatch
    {
        public static void Prefix(GuiDialogHandbook __instance)
        {
            HandbookFilterCachePatch.Clear(__instance);
            HandbookOpenCachePatch.Clear(__instance);
        }
    }

    [HarmonyPatch(typeof(GuiDialogHandbook), "LoadPages_Async")]
    internal static class HandbookLoadPagesCacheInvalidationPatch
    {
        public static void Postfix(GuiDialogHandbook __instance)
        {
            HandbookFilterCachePatch.Clear(__instance);
        }
    }

    [HarmonyPatch(typeof(GuiDialogHandbook), nameof(GuiDialogHandbook.OnGuiClosed))]
    internal static class HandbookCloseCachePatch
    {
        private static readonly AccessTools.FieldRef<GuiDialogHandbook, Stack<BrowseHistoryElement>> BrowseHistory =
            AccessTools.FieldRefAccess<GuiDialogHandbook, Stack<BrowseHistoryElement>>("browseHistory");

        private static readonly AccessTools.FieldRef<GuiDialogHandbook, string> CurrentSearchText =
            AccessTools.FieldRefAccess<GuiDialogHandbook, string>("currentSearchText");

        private static readonly AccessTools.FieldRef<GuiDialogHandbook, GuiComposer> OverviewGui =
            AccessTools.FieldRefAccess<GuiDialogHandbook, GuiComposer>("overviewGui");

        private static readonly AccessTools.FieldRef<GuiDialog, ICoreClientAPI> ClientApi =
            AccessTools.FieldRefAccess<GuiDialog, ICoreClientAPI>("capi");

        public static bool Prefix(GuiDialogHandbook __instance)
        {
            ICoreClientAPI capi = ClientApi(__instance);

            try
            {
                BrowseHistory(__instance).Clear();
                CurrentSearchText(__instance) = "";
                ClearSearchFieldWithoutFiltering(OverviewGui(__instance));

                if (capi.IsSinglePlayer && !capi.OpenedToLan && !capi.Settings.Bool["noHandbookPause"] && capi.OpenedGuis.FirstOrDefault(dlg => dlg is GuiDialogCreateCharacter) == null)
                {
                    capi.PauseGame(false);
                }

                return false;
            }
            catch (Exception ex)
            {
                HandbookCacheDiagnostics.LogFailure(capi, "Fast close failed; falling back to vanilla close: {0}", ex);
                return true;
            }
        }

        private static void ClearSearchFieldWithoutFiltering(GuiComposer overviewGui)
        {
            GuiElementTextInput searchField = overviewGui?.GetTextInput("searchField");
            if (searchField == null) return;

            Action<string> onTextChanged = searchField.OnTextChanged;
            searchField.OnTextChanged = null;

            try
            {
                searchField.SetValue("", true);
            }
            finally
            {
                searchField.OnTextChanged = onTextChanged;
            }
        }
    }
}
