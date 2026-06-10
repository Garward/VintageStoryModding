using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace VintageKinematics.Gui
{
    internal class MachineRecipeBrowser<T> : IDisposable where T : class, IRecipeBrowserListItem
    {
        private readonly string titleLangCode;
        private readonly string searchPlaceholderLangCode;
        private readonly Func<IEnumerable<T>> buildItems;
        private readonly Action<T> onClicked;
        private readonly Func<GuiComposer> getComposer;
        private readonly string titleKey;
        private readonly string searchKey;
        private readonly string sortKey;
        private readonly string listKey;
        private readonly string scrollbarKey;
        private readonly int cellHeight;
        private readonly string[] sortValues;
        private readonly string[] sortNames;

        private List<T> items = new List<T>();
        private ElementBounds titleBounds;
        private ElementBounds searchBounds;
        private ElementBounds sortBounds;
        private ElementBounds listBounds;
        private ElementBounds clipBounds;
        private ElementBounds insetBounds;
        private ElementBounds scrollbarBounds;

        public bool IsOpen { get; private set; }
        public string SearchText { get; private set; } = "";
        public string SortValue { get; private set; } = "";
        public double Width { get; }
        public double ListHeight { get; }

        public MachineRecipeBrowser(
            string keyPrefix,
            string titleLangCode,
            string searchPlaceholderLangCode,
            Func<IEnumerable<T>> buildItems,
            Action<T> onClicked,
            Func<GuiComposer> getComposer,
            double width = 560.0,
            double listHeight = 292.0,
            int cellHeight = 64,
            string[] sortValues = null,
            string[] sortNames = null)
        {
            this.titleLangCode = titleLangCode;
            this.searchPlaceholderLangCode = searchPlaceholderLangCode;
            this.buildItems = buildItems;
            this.onClicked = onClicked;
            this.getComposer = getComposer;
            this.cellHeight = cellHeight;
            this.sortValues = sortValues;
            this.sortNames = sortNames;
            Width = width;
            ListHeight = listHeight;
            titleKey = keyPrefix + "-title";
            searchKey = keyPrefix + "-search";
            sortKey = keyPrefix + "-sort";
            listKey = keyPrefix + "-list";
            scrollbarKey = keyPrefix + "-scrollbar";
        }

        public bool Toggle(Action recompose)
        {
            IsOpen = !IsOpen;
            if (!IsOpen) ClearItems();
            recompose?.Invoke();
            return true;
        }

        public void SetBounds(double x, double y)
        {
            titleBounds = ElementBounds.Fixed(x, y, Width, 22.0);
            if (HasSort)
            {
                double sortWidth = 158.0;
                searchBounds = ElementBounds.Fixed(x, y + 26.0, Width - sortWidth - 34.0, 28.0);
                sortBounds = ElementBounds.Fixed(x + Width - sortWidth - 26.0, y + 26.0, sortWidth, 28.0);
            }
            else
            {
                searchBounds = ElementBounds.Fixed(x, y + 26.0, Width - 26.0, 28.0);
            }
            listBounds = ElementBounds.Fixed(x, y + 64.0, Width - 28.0, ListHeight);
            clipBounds = listBounds.ForkBoundingParent();
            insetBounds = listBounds.FlatCopy().FixedGrow(6.0).WithFixedOffset(-3.0, -3.0);
            scrollbarBounds = insetBounds.CopyOffsetedSibling(listBounds.fixedWidth + 7.0).WithFixedWidth(20.0);
        }

        public void AddBounds(List<ElementBounds> childBounds)
        {
            if (!IsOpen) return;
            childBounds.Add(titleBounds);
            childBounds.Add(searchBounds);
            if (HasSort) childBounds.Add(sortBounds);
            childBounds.Add(insetBounds);
            childBounds.Add(clipBounds);
            childBounds.Add(scrollbarBounds);
        }

        public GuiComposer AddToComposer(GuiComposer composer)
        {
            if (!IsOpen) return composer;

            ClearItems();
            items = BuildFilteredItems();
            composer = composer
                .AddStaticText(Lang.Get(titleLangCode), CairoFont.WhiteSmallText(), titleBounds, titleKey)
                .AddTextInput(searchBounds, OnSearchChanged, CairoFont.WhiteSmallishText(), searchKey);

            if (HasSort)
            {
                composer = composer.AddDropDown(sortValues, sortNames, CurrentSortIndex(), OnSortChanged, sortBounds, sortKey);
            }

            composer = composer
                .BeginClip(clipBounds)
                    .AddInset(insetBounds, 3)
                    .AddFlatList(listBounds, OnListClicked, AsFlatListItems(items), listKey)
                .EndClip()
                .AddVerticalScrollbar(OnScrollbarValue, scrollbarBounds, scrollbarKey);

            ConfigureListHeight(composer.GetFlatList(listKey));
            return composer;
        }

        public void AfterCompose(GuiComposer composer)
        {
            if (!IsOpen || composer == null) return;

            GuiElementTextInput searchInput = composer.GetTextInput(searchKey);
            searchInput.SetPlaceHolderText(Lang.Get(searchPlaceholderLangCode));
            if (!string.IsNullOrEmpty(SearchText)) searchInput.SetValue(SearchText, false);
            RefreshScrollbar(composer);
        }

        private void OnSearchChanged(string text)
        {
            SearchText = text ?? "";
            RefreshItems();
        }

        private void OnSortChanged(string code, bool selected)
        {
            if (!selected) return;
            SortValue = code ?? "";
            RefreshItems();
        }

        private void RefreshItems()
        {
            if (!IsOpen) return;
            GuiComposer composer = getComposer?.Invoke();
            GuiElementFlatList list = composer?.GetFlatList(listKey);
            if (list == null) return;

            DisposeItems(items);
            items = BuildFilteredItems();
            list.Elements = AsFlatListItems(items);
            ConfigureListHeight(list);
            list.CalcTotalHeight();
            RefreshScrollbar(composer);
        }

        private void OnListClicked(int index)
        {
            if (index < 0 || index >= items.Count) return;
            onClicked?.Invoke(items[index]);
        }

        private void OnScrollbarValue(float value)
        {
            GuiElementFlatList list = getComposer?.Invoke()?.GetFlatList(listKey);
            if (list == null) return;
            list.insideBounds.fixedY = -value;
            list.insideBounds.CalcWorldBounds();
        }

        private void RefreshScrollbar(GuiComposer composer)
        {
            GuiElementFlatList list = composer?.GetFlatList(listKey);
            GuiElementScrollbar scrollbar = composer?.GetScrollbar(scrollbarKey);
            if (list == null || scrollbar == null) return;

            ConfigureListHeight(list);
            list.CalcTotalHeight();
            scrollbar.SetHeights((float)ListHeight, (float)list.insideBounds.fixedHeight);
            scrollbar.CurrentYPosition = 0f;
            OnScrollbarValue(0f);
        }

        private void ConfigureListHeight(GuiElementFlatList list)
        {
            if (list == null) return;
            list.unscaledCellHeight = cellHeight;
        }

        private List<T> BuildFilteredItems()
        {
            List<T> result = new List<T>();
            IEnumerable<T> built = buildItems?.Invoke();
            if (built == null) return result;

            foreach (T item in built)
            {
                if (item == null) continue;
                if (item.SearchScore(SearchText) < int.MaxValue)
                {
                    result.Add(item);
                }
                else
                {
                    item.Dispose();
                }
            }

            result.Sort((left, right) =>
            {
                int scoreCompare = left.SearchScore(SearchText).CompareTo(right.SearchScore(SearchText));
                if (scoreCompare != 0) return scoreCompare;
                string sortMode = CurrentSortValue();
                int keyCompare = string.Compare(left.SortKey(sortMode), right.SortKey(sortMode), StringComparison.OrdinalIgnoreCase);
                if (keyCompare != 0) return keyCompare;
                return string.Compare(left.SortTitle, right.SortTitle, StringComparison.OrdinalIgnoreCase);
            });

            return result;
        }

        private int CurrentSortIndex()
        {
            if (!HasSort) return 0;
            for (int i = 0; i < sortValues.Length; i++)
            {
                if (string.Equals(sortValues[i], SortValue, StringComparison.Ordinal)) return i;
            }
            return 0;
        }

        private string CurrentSortValue()
        {
            if (!HasSort) return "";
            string value = SortValue;
            if (string.IsNullOrEmpty(value)) value = sortValues[0];
            return value ?? "";
        }

        private bool HasSort => sortValues != null && sortNames != null && sortValues.Length == sortNames.Length && sortValues.Length > 0;

        private static List<IFlatListItem> AsFlatListItems(List<T> source)
        {
            List<IFlatListItem> flatItems = new List<IFlatListItem>(source.Count);
            for (int i = 0; i < source.Count; i++) flatItems.Add(source[i]);
            return flatItems;
        }

        private static void DisposeItems(List<T> source)
        {
            if (source == null) return;
            for (int i = 0; i < source.Count; i++) source[i]?.Dispose();
        }

        private void ClearItems()
        {
            DisposeItems(items);
            items = new List<T>();
        }

        public void Dispose()
        {
            ClearItems();
        }
    }
}
