using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using VintageKinematics.Api.Storage;
using VintageKinematics.Storage.Terminal;

namespace VintageKinematics.Gui.Storage
{
    public sealed partial class GuiDialogStorageTerminal : GuiDialogBlockEntity
    {
        private const string StatusKey = "storageStatus";
        private const string CapacityKey = "storageCapacity";
        private const string PageKey = "storagePage";
        private const string GridKey = "storageGrid";
        private const string SearchKey = "storageSearch";
        private const string PreviousKey = "storagePrevious";
        private const string NextKey = "storageNext";
        private const double ResizeHitHeight = 20.0;
        private const int PredictionTimeoutMs = 3000;

        private readonly Action<StorageTerminalQuery> requestPage;
        private readonly Action<StorageTerminalActionRequest> requestAction;
        private StorageTerminalPage page;
        private StorageTerminalPage confirmedPage;
        private StorageTerminalInventoryPrediction inventoryPrediction;
        private long pendingActionRequestId;
        private long nextRequestId;
        private long searchGeneration;
        private StorageTerminalLayout layout;
        private StorageEntryGridElement grid;
        private int visibleRows = StorageTerminalResizeModel.MinRows;
        private bool resizing;
        private int resizeStartMouseY;
        private int resizeStartRows;
        private int resizeMaximumRows;
        private long resizeGeneration;

        public override double DrawOrder => 0.2;

        public GuiDialogStorageTerminal(
            string title,
            BlockPos pos,
            StorageTerminalPage initialPage,
            Action<StorageTerminalQuery> requestPage,
            Action<StorageTerminalActionRequest> requestAction,
            ICoreClientAPI capi)
            : base(title, pos, capi)
        {
            page = initialPage ?? throw new ArgumentNullException(nameof(initialPage));
            confirmedPage = page;
            this.requestPage = requestPage;
            this.requestAction = requestAction;
            nextRequestId = page.RequestId;
            if (IsDuplicate) return;
            ComposeDialog(title);
        }

        public void UpdatePage(StorageTerminalPage updated)
        {
            if (updated == null || updated.RequestId < nextRequestId) return;
            confirmedPage = updated;
            page = updated;
            inventoryPrediction?.Confirm();
            inventoryPrediction = null;
            if (updated.RequestId >= pendingActionRequestId) pendingActionRequestId = 0;
            nextRequestId = Math.Max(nextRequestId, page.RequestId);
            RefreshPageView();
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            StorageTerminalShiftClickRouter.Activate(this, TryDepositInventorySlot);
        }

        public override void OnGuiClosed()
        {
            StorageTerminalShiftClickRouter.Deactivate(this);
            base.OnGuiClosed();
        }

        public override void Dispose()
        {
            StorageTerminalShiftClickRouter.Deactivate(this);
            base.Dispose();
        }

        private void ComposeDialog(string title)
        {
            layout = new StorageTerminalLayout(visibleRows);
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.CenterMiddle);
            string[] sortValues = { "name", "quantitydesc", "quantityasc" };
            string[] sortNames =
            {
                Lang.Get("vintagekinematics:storage-terminal-sort-name"),
                Lang.Get("vintagekinematics:storage-terminal-sort-most"),
                Lang.Get("vintagekinematics:storage-terminal-sort-least")
            };
            grid = new StorageEntryGridElement(
                capi,
                layout.Grid,
                visibleRows,
                DepositHeld,
                WithdrawEntry);

            SingleComposer = capi.Gui.CreateCompo("vk-storage-terminal-" + BlockEntityPosition, dialogBounds)
                .AddShadedDialogBG(layout.Background, true, 10, 0.86f)
                .AddDialogTitleBar(title, CloseIconPressed)
                .BeginChildElements(layout.Background)
                    .AddDynamicText(StatusText(), CairoFont.WhiteSmallText(), layout.Status, StatusKey)
                    .AddDynamicText(CapacityText(), StorageTerminalTheme.MutedText(), layout.Capacity, CapacityKey)
                    .AddTextInput(layout.Search, OnSearchChanged, CairoFont.WhiteSmallishText(), SearchKey)
                    .AddDropDown(sortValues, sortNames, (int)page.Sort, OnSortChanged, layout.Sort, "storageSort")
                    .AddInset(layout.GridInset, 3)
                    .AddInteractiveElement(grid, GridKey)
                    .AddSmallButton(Lang.Get("vintagekinematics:storage-terminal-previous"), PreviousPage, layout.Previous, EnumButtonStyle.Normal, PreviousKey)
                    .AddDynamicText(PageText(), CairoFont.WhiteSmallText(), layout.PageText, PageKey)
                    .AddSmallButton(Lang.Get("vintagekinematics:storage-terminal-next"), NextPage, layout.Next, EnumButtonStyle.Normal, NextKey)
                    .AddStaticText(Lang.Get("vintagekinematics:storage-terminal-transfer-help"), StorageTerminalTheme.MutedText(), layout.Footer)
                    .AddInteractiveElement(new StorageResizeGripElement(capi, layout.ResizeGrip), "storageResizeGrip")
                .EndChildElements()
                .Compose();

            SingleComposer.GetTextInput(SearchKey)?.SetPlaceHolderText(
                Lang.Get("vintagekinematics:storage-terminal-search"));
            grid.UpdateEntries(page.Entries);
            RefreshPageButtons();
        }

        public override void OnMouseDown(MouseEvent args)
        {
            if (TryStartResize(args))
            {
                args.Handled = true;
                return;
            }
            base.OnMouseDown(args);
        }

        public override void OnMouseMove(MouseEvent args)
        {
            if (resizing)
            {
                int rows = StorageTerminalResizeModel.RowsForDrag(
                    resizeStartRows,
                    args.Y - resizeStartMouseY,
                    RuntimeEnv.GUIScale,
                    resizeMaximumRows);
                ApplyVisibleRows(rows);
                args.Handled = true;
                return;
            }
            base.OnMouseMove(args);
        }

        public override void OnMouseUp(MouseEvent args)
        {
            if (resizing)
            {
                resizing = false;
                resizeGeneration++;
                RequestVisiblePage();
                args.Handled = true;
                return;
            }
            base.OnMouseUp(args);
        }

        private void OnSearchChanged(string search)
        {
            long generation = ++searchGeneration;
            capi.Event.RegisterCallback(_ =>
            {
                if (generation != searchGeneration || !IsOpened()) return;
                SendQuery(search, 0, page.Sort);
            }, 200);
        }

        private void OnSortChanged(string code, bool selected)
        {
            if (!selected) return;
            StorageTerminalSort sort = code switch
            {
                "quantitydesc" => StorageTerminalSort.QuantityDescending,
                "quantityasc" => StorageTerminalSort.QuantityAscending,
                _ => StorageTerminalSort.Name
            };
            SendQuery(CurrentSearch(), 0, sort);
        }

        private bool PreviousPage()
        {
            if (page.Page > 0) SendQuery(CurrentSearch(), page.Page - 1, page.Sort);
            return true;
        }

        private bool NextPage()
        {
            if (page.Page + 1 < page.PageCount)
            {
                SendQuery(CurrentSearch(), page.Page + 1, page.Sort);
            }
            return true;
        }

        private void SendQuery(string search, int targetPage, StorageTerminalSort sort)
        {
            if (pendingActionRequestId > 0) return;
            requestPage?.Invoke(new StorageTerminalQuery(
                ++nextRequestId,
                search,
                targetPage,
                sort,
                VisiblePageSize()));
        }

        private string CurrentSearch()
        {
            return SingleComposer?.GetTextInput(SearchKey)?.GetText() ?? page.Search;
        }

        private void RefreshPageButtons()
        {
            GuiElementTextButton previous = SingleComposer?.GetButton(PreviousKey);
            GuiElementTextButton next = SingleComposer?.GetButton(NextKey);
            bool canInteract = pendingActionRequestId <= 0;
            if (previous != null) previous.Enabled = canInteract && page.Page > 0;
            if (next != null) next.Enabled = canInteract && page.Page + 1 < page.PageCount;
        }

        private string StatusText()
        {
            if (pendingActionRequestId > 0)
            {
                return Lang.Get("vintagekinematics:storage-terminal-pending");
            }
            string state = page.Stats.State == StorageState.Online
                && page.Stats.PowerRequired
                && !page.Stats.Powered
                    ? Lang.Get("vintagekinematics:storage-state-unpowered")
                    : Lang.Get("vintagekinematics:storage-state-" + page.Stats.State.ToString().ToLowerInvariant());
            return Lang.Get("vintagekinematics:storage-terminal-status", state, page.MatchingEntries);
        }

        private string CapacityText()
        {
            return Lang.Get(
                "vintagekinematics:storage-terminal-capacity",
                page.Stats.StoredItems.ToString("N0"),
                page.Stats.ItemCapacity.ToString("N0"),
                page.Stats.EntryCount.ToString("N0"));
        }

        private string PageText()
        {
            return Lang.Get(
                "vintagekinematics:storage-terminal-page",
                page.Page + 1,
                page.PageCount);
        }

        private bool TryStartResize(MouseEvent args)
        {
            if (args.Button != EnumMouseButton.Left || SingleComposer == null) return false;
            ElementBounds bounds = SingleComposer.Bounds;
            double localY = args.Y - bounds.absY;
            if (localY < bounds.OuterHeight - GuiElement.scaled(ResizeHitHeight)
                || localY > bounds.OuterHeight)
            {
                return false;
            }

            ForceFixedDialogPosition(bounds);
            resizing = true;
            resizeStartMouseY = args.Y;
            resizeStartRows = visibleRows;
            double availableBelow = capi.Render.FrameHeight - (bounds.absY + bounds.OuterHeight);
            resizeMaximumRows = StorageTerminalResizeModel.MaximumRowsThatFit(
                visibleRows,
                availableBelow,
                RuntimeEnv.GUIScale);
            return true;
        }

        private void ApplyVisibleRows(int rows)
        {
            if (rows == visibleRows || SingleComposer == null) return;
            visibleRows = rows;
            layout.SetRows(rows);
            grid.SetRows(rows);
            SingleComposer.ReCompose();
            QueueVisiblePageRefresh();
        }

        private void QueueVisiblePageRefresh()
        {
            long generation = ++resizeGeneration;
            capi.Event.RegisterCallback(_ =>
            {
                if (generation != resizeGeneration || !IsOpened()) return;
                RequestVisiblePage();
            }, 150);
        }

        private void RequestVisiblePage()
        {
            int firstVisibleEntry = page.Page * page.PageSize;
            int targetPage = firstVisibleEntry / VisiblePageSize();
            SendQuery(CurrentSearch(), targetPage, page.Sort);
        }

        private int VisiblePageSize()
        {
            return visibleRows * StorageTerminalTheme.Columns;
        }

        private void RefreshPageView()
        {
            if (SingleComposer == null) return;
            SingleComposer.GetDynamicText(StatusKey)?.SetNewText(StatusText());
            SingleComposer.GetDynamicText(CapacityKey)?.SetNewText(CapacityText());
            SingleComposer.GetDynamicText(PageKey)?.SetNewText(PageText());
            grid?.UpdateEntries(page.Entries);
            RefreshPageButtons();
        }

        private static void ForceFixedDialogPosition(ElementBounds bounds)
        {
            if (bounds.Alignment == EnumDialogArea.None) return;
            bounds.fixedX = bounds.absX / RuntimeEnv.GUIScale;
            bounds.fixedY = bounds.absY / RuntimeEnv.GUIScale;
            bounds.fixedOffsetX = 0;
            bounds.fixedOffsetY = 0;
            bounds.Alignment = EnumDialogArea.None;
            bounds.absMarginX = 0;
            bounds.absMarginY = 0;
            bounds.CalcWorldBounds();
        }
    }
}
