using System;
using VRPG.Client.PlayerMenu.Shell;
using VRPG.Client.PlayerMenu.Components;
using VRPG.Client.PlayerMenu.Inventory;
using VRPG.Inventory;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VRPG.Client.PlayerMenu;

/// <summary>Single native composition host; gallery mode is explicitly separate from future production page adapters.</summary>
public sealed class GuiDialogVrpgPlayerMenu : GuiDialog
{
    private readonly PlayerMenuController controller;
    private readonly PlayerMenuGallery? gallery;
    private InventoryPage? Inventory => controller.Inventory;
    private bool inventoryDirty;
    private PlayerMenuShell? shell;
    private PlayerMenuLayout? layout;
    public override string? ToggleKeyCombinationCode => null;
    public GuiDialogVrpgPlayerMenu(ICoreClientAPI api, PlayerMenuController controller, PlayerMenuGallery gallery) : base(api)
    { this.controller = controller; this.gallery = gallery; }
    public GuiDialogVrpgPlayerMenu(ICoreClientAPI api, PlayerMenuController controller) : base(api)
    {
        this.controller = controller;
        if (controller.Inventory == null) throw new ArgumentException("An inventory page is required.", nameof(controller));
        controller.Inventory.Changed += OnInventoryChanged;
    }
    private void OnInventoryChanged() => inventoryDirty = true;
    public override void OnGuiOpened()
    {
        if (!controller.Session.IsOpen) controller.Enter(PlayerMenuEntryPoint.InventoryMenu);
        Inventory?.Open(Environment.TickCount64); Compose(); base.OnGuiOpened();
    }
    public override void OnGuiClosed() { Inventory?.Close(); controller.Close(); base.OnGuiClosed(); }
    private void Compose()
    {
        layout = PlayerMenuLayout.ForViewport(capi.Render.FrameWidth, capi.Render.FrameHeight, RuntimeEnv.GUIScale);
        gallery?.Equipment.SetExpanded(controller.Session.Current.EquipmentExpanded, controller.Session.ReducedMotion);
        var bounds = ElementBounds.Fixed(0, 0, layout.Width, layout.Height);
        shell = new(capi, bounds, layout, controller.Session, gallery, OnIntent, Inventory);
        SingleComposer?.Dispose();
        var composer = capi.Gui.CreateCompo(gallery != null ? "vrpg-player-menu-gallery" : "vrpg-player-menu", ElementStdBounds.AutosizedMainDialog)
            .BeginChildElements(ElementBounds.Fixed(0, 0, layout.Width, layout.Height))
            .AddInteractiveElement(shell, "shell");
        if ((!layout.AlternatePanes || !controller.Session.Current.InspectorOnly) && (Inventory == null || controller.Session.Page == PlayerMenuPage.Inventory))
        {
            var searchBounds = Inventory == null ? layout.BrowserSearch : layout.InventorySearch;
            composer.AddTextInput(ElementBounds.Fixed(searchBounds.X, searchBounds.Y, searchBounds.Width, searchBounds.Height), value =>
            {
                var input = new SearchField(); input.SetText(value ?? "");
                if (input.Text != value) SingleComposer?.GetTextInput("search")?.SetValue(input.Text, false);
                controller.Session.Update(controller.Session.Current with { Search = input.Text, ScrollAnchor = 0 });
                Inventory?.ChangeQuery(Inventory.Query with { Search = input.Text, Offset = 0 }, Environment.TickCount64);
                shell?.Refresh();
            }, CairoFont.WhiteSmallishText(), "search");
        }
        if (Inventory != null && controller.Session.Page == PlayerMenuPage.Inventory && (!layout.AlternatePanes || controller.Session.Current.InspectorOnly))
        {
            var field = layout.PrimaryAction;
            composer.AddTextInput(ElementBounds.Fixed(field.X, field.Y, field.Width * .56, field.Height),
                _ => { }, CairoFont.WhiteSmallishText(), "quantity");
        }
        SingleComposer = composer.EndChildElements().Compose();
        var search = SingleComposer.GetTextInput("search");
        search?.SetValue(controller.Session.Current.Search, false);
        search?.SetPlaceHolderText(PlayerMenuShell.T(Inventory == null ? "search" : "item-search"));
        SingleComposer.GetTextInput("quantity")?.SetValue(Inventory?.View.Selected is { } row
            ? InventoryItemRow.ExactQuantity(Inventory.View.Selection.Amounts.TryGetValue(row.EntryId, out long chosen) ? chosen : row.Quantity - row.ReservedQuantity) : "", false);
    }
    private void OnIntent(string intent)
    {
        var session = controller.Session;
        if (intent is "focus-search" or "search" or "quantity")
        {
            var input = SingleComposer?.GetTextInput(intent == "quantity" ? "quantity" : "search");
            if (input != null) SingleComposer?.FocusElement(input.TabIndex);
            return;
        }
        if (intent == "close") { TryClose(); return; }
        if (Inventory != null && HandleInventoryIntent(intent)) { Compose(); return; }
        if (intent == "back") { session.Update(session.Current with { Focus = "" }); controller.Back(layout?.AlternatePanes == true); if (!session.IsOpen) { TryClose(); return; } }
        else if (intent.StartsWith("page:", StringComparison.Ordinal)) session.Navigate(Enum.Parse<PlayerMenuPage>(intent[5..]));
        else if (intent.StartsWith("entry:", StringComparison.Ordinal)) session.Update(session.Current with { SelectedEntry = intent[6..], InspectorOnly = layout?.AlternatePanes == true, InspectorScroll = 0 });
        else if (intent == "category-next")
        {
            var categories = TypedTabStrip.Categories;
            session.Update(session.Current with { Category = categories[(Array.IndexOf(categories, session.Current.Category) + 1) % categories.Length], SelectedEntry = "", ScrollAnchor = 0, EquipmentExpanded = false });
        }
        else if (intent.StartsWith("category:", StringComparison.Ordinal))
        {
            session.Update(session.Current with { Category = intent[9..], SelectedEntry = "", ScrollAnchor = 0, EquipmentExpanded = false });
        }
        else if (intent == "equipment") session.Update(session.Current with { EquipmentExpanded = !session.Current.EquipmentExpanded });
        else if (intent == "inspect") session.Update(session.Current with { InspectorOnly = true });
        else if (intent == "state") gallery?.CycleState();
        else if (intent == "count") { gallery?.CycleCount(); session.Update(session.Current with { ScrollAnchor = 0 }); }
        else if (intent == "labels") gallery?.ToggleLabels();
        else if (intent == "motion") session.ReducedMotion = !session.ReducedMotion;
        else if (intent == "action") gallery?.Activate();
        Compose();
    }
    private bool HandleInventoryIntent(string intent)
    {
        var page = Inventory!; var session = controller.Session; double now = Environment.TickCount64;
        var query = page.Query;
        if (intent.StartsWith("entry:", StringComparison.Ordinal))
        {
            page.Select(intent[6..]);
            session.Update(session.Current with { SelectedEntry = page.View.SelectedId, InspectorOnly = layout?.AlternatePanes == true, InspectorScroll = 0 });
        }
        else if (intent == "quantity") { var input = SingleComposer?.GetTextInput("quantity"); if (input != null) SingleComposer?.FocusElement(input.TabIndex); }
        else if (intent == "select-quantity")
        {
            if (!page.View.Stale && !page.View.Loading && page.View.Selected is { } row
                && long.TryParse(SingleComposer?.GetTextInput("quantity")?.GetText(), out long quantity)
                && page.View.Selection.Set(row, quantity)) page.View.DetailError = "";
            else page.View.DetailError = "invalid-quantity";
        }
        else if (intent == "select-page") { if (!page.View.Stale && !page.View.Loading) page.View.Selection.SelectPage(page.View.Entries); }
        else if (intent == "clear-selection") page.View.Selection.Clear();
        else if (intent == "retry-detail") page.Select(page.View.SelectedId);
        else if (intent == "refresh") page.Refresh(now);
        else if (intent == "next-page") { page.NextPage(now); session.Update(session.Current with { ScrollAnchor = 0 }); }
        else if (intent == "previous-page") { page.PreviousPage(now); session.Update(session.Current with { ScrollAnchor = 0 }); }
        else if (intent == "view") { page.View.Grid = !page.View.Grid; session.Update(session.Current with { ScrollAnchor = 0 }); }
        else
        {
            if (intent.StartsWith("category:", StringComparison.Ordinal)) query = query with { Category = intent[9..], Offset = 0 };
            else if (intent == "category-next")
            {
                var codes = new[] { "" }.Concat(page.View.Categories.Select(x => x.Code)).ToArray();
                query = query with { Category = codes[(Array.IndexOf(codes, query.Category) + 1) % codes.Length], Offset = 0 };
            }
            else if (intent == "more-types") query = query with { CategoryOffset = query.CategoryOffset + 64 < page.View.CategoryTotal ? query.CategoryOffset + 64 : 0 };
            else if (intent == "addon-type")
            {
                var codes = page.View.Categories.Where(x => !PlayerItemCategories.Required.Contains(x.Code)).Select(x => x.Code).ToArray();
                if (codes.Length > 0) query = query with { Category = codes[(Array.IndexOf(codes, query.Category) + 1) % codes.Length], Offset = 0 };
            }
            else if (intent == "location") query = query with { Location = (PlayerItemLocation)(((int)query.Location + 1) % 4), Category = "", Offset = 0, CategoryOffset = 0 };
            else if (intent == "filter")
            {
                var filters = new[] { PlayerItemFilters.None, PlayerItemFilters.Favorite, PlayerItemFilters.Protected, PlayerItemFilters.Available, PlayerItemFilters.Reserved };
                query = query with { Filters = filters[(Array.IndexOf(filters, query.Filters) + 1) % filters.Length], Offset = 0 };
            }
            else if (intent == "sort") query = query with { Sort = (PlayerItemSortOrder)(((int)query.Sort + 1) % 3), Offset = 0 };
            else return false;
            page.ChangeQuery(query, now);
            session.Update(session.Current with { Category = query.Category, ScrollAnchor = 0, SelectedEntry = "", InspectorScroll = 0 });
        }
        return true;
    }
    public override void OnKeyDown(KeyEvent args)
    {
        var textField = SingleComposer?.GetTextInput("quantity")?.HasFocus == true
            ? SingleComposer.GetTextInput("quantity") : SingleComposer?.GetTextInput("search");
        bool typing = textField?.HasFocus == true;
        if (args.KeyCode == (int)GlKeys.Escape)
        {
            if (typing) { textField?.OnFocusLost(); args.Handled = true; return; }
            OnIntent("back"); args.Handled = true; return;
        }
        // Let the actual native text field consume letters before semantic menu actions.
        if (typing)
        {
            if (args.KeyCode == (int)GlKeys.Tab)
            {
                SingleComposer?.UnfocusOwnElements(); shell?.AdvanceFocus(args.ShiftPressed);
            }
            else textField?.OnKeyDown(capi, args);
            args.Handled = true; return;
        }
        foreach (string code in new[] { VanillaMenuActions.Inventory, VanillaMenuActions.Character })
        {
            var hotkey = capi.Input.GetHotKeyByCode(code);
            if (hotkey?.DidPress(args, capi.World, capi.World.Player, true) == true)
            { TryClose(); args.Handled = true; return; }
        }
        base.OnKeyDown(args);
    }
    public override void OnRenderGUI(float deltaTime)
    {
        Inventory?.Pump(Environment.TickCount64);
        if (inventoryDirty)
        {
            inventoryDirty = false;
            // Responses rebuild the cached shell, preserving the native text field's caret and value.
            shell?.Refresh();
        }
        var next = PlayerMenuLayout.ForViewport(capi.Render.FrameWidth, capi.Render.FrameHeight, RuntimeEnv.GUIScale);
        if (layout != next) Compose();
        base.OnRenderGUI(deltaTime);
    }
    public override void Dispose()
    {
        if (Inventory != null) { Inventory.Changed -= OnInventoryChanged; Inventory.Dispose(); }
        base.Dispose();
    }
}
