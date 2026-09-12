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
    private bool hubDirty;
    private bool partyDirty;
    private bool composedPartyOpen;
    private bool composedPartySearch;
    private bool restoreInventorySearchFocus;
    private PlayerMenuShell? shell;
    private PlayerMenuLayout? layout;
    public override string? ToggleKeyCombinationCode => null;
    public GuiDialogVrpgPlayerMenu(ICoreClientAPI api, PlayerMenuController controller, PlayerMenuGallery gallery) : base(api)
    { this.controller = controller; this.gallery = gallery; }
    public GuiDialogVrpgPlayerMenu(ICoreClientAPI api, PlayerMenuController controller) : base(api)
    {
        this.controller = controller;
        if (controller.Inventory == null && controller.Party == null) throw new ArgumentException("An inventory or party adapter is required.", nameof(controller));
        if (controller.Inventory != null) controller.Inventory.Changed += OnInventoryChanged;
        if (controller.Party != null) controller.Party.Changed += OnPartyChanged;
        if (controller.Hub != null) controller.Hub.Changed += OnHubChanged;
    }
    private void OnHubChanged() => hubDirty = true;
    public void ShowPage(PlayerMenuPage page)
    {
        if (!controller.Session.IsOpen) controller.Enter(PlayerMenuEntryPoint.InventoryMenu);
        controller.Navigate(page);
        if (IsOpened()) Compose();
        else if (!TryOpen()) controller.Close();
    }
    private void OnInventoryChanged() => inventoryDirty = true;
    private void OnPartyChanged() => partyDirty = true;
    public override void OnGuiOpened()
    {
        if (!controller.Session.IsOpen) controller.Enter(PlayerMenuEntryPoint.InventoryMenu);
        Inventory?.Open(capi.World.ElapsedMilliseconds); Compose(); base.OnGuiOpened();
    }
    public override void OnGuiClosed() { Inventory?.Close(); controller.Close(); base.OnGuiClosed(); }
    private void Compose()
    {
        layout = PlayerMenuLayout.ForViewport(capi.Render.FrameWidth, capi.Render.FrameHeight, RuntimeEnv.GUIScale);
        gallery?.Equipment.SetExpanded(controller.Session.Current.EquipmentExpanded, controller.Session.ReducedMotion);
        var bounds = ElementBounds.Fixed(0, 0, layout.Width, layout.Height);
        bool partyOpen = controller.Party?.State.IsOpen == true;
        if (!composedPartyOpen && partyOpen)
        {
            restoreInventorySearchFocus = SingleComposer?.GetTextInput("search")?.HasFocus == true || controller.Party?.State.ReturnContext?.Focus == "search";
        }
        bool returning = composedPartyOpen && !partyOpen;
        composedPartyOpen = partyOpen;
        shell = new(capi, bounds, layout, controller.Session, gallery, OnIntent, Inventory, controller.Party, hub: controller.Hub);
        SingleComposer?.Dispose();
        var composer = capi.Gui.CreateCompo(gallery != null ? "vrpg-player-menu-gallery" : "vrpg-player-menu", ElementStdBounds.AutosizedMainDialog)
            .BeginChildElements(ElementBounds.Fixed(0, 0, layout.Width, layout.Height))
            .AddInteractiveElement(shell, "shell");
        if (!partyOpen && (Inventory != null || gallery != null) && (!layout.AlternatePanes || !controller.Session.Current.InspectorOnly) && (Inventory == null || controller.Session.Page == PlayerMenuPage.Inventory))
        {
            var searchBounds = Inventory == null ? layout.BrowserSearch : layout.InventorySearch;
            composer.AddTextInput(ElementBounds.Fixed(searchBounds.X, searchBounds.Y, searchBounds.Width, searchBounds.Height), value =>
            {
                var input = new SearchField(); input.SetText(value ?? "");
                if (input.Text != value) SingleComposer?.GetTextInput("search")?.SetValue(input.Text, false);
                controller.Session.Update(controller.Session.Current with { Search = input.Text, ScrollAnchor = 0, Focus = "search" });
                Inventory?.ChangeQuery(Inventory.Query with { Search = input.Text, Offset = 0 }, capi.World.ElapsedMilliseconds);
                shell?.Refresh();
            }, CairoFont.WhiteSmallishText(), "search");
        }
        composedPartySearch = controller.Party?.ShowSearch(layout.AlternatePanes) == true;
        if (composedPartySearch)
        {
            var field = shell.PartySearchBounds;
            composer.AddTextInput(ElementBounds.Fixed(field.X, field.Y, field.Width, field.Height), value =>
            {
                controller.Party!.SetSearchText(value ?? "");
                if (value != controller.Party.State.Search) SingleComposer?.GetTextInput("party-search")?.SetValue(controller.Party.State.Search, false);
            }, CairoFont.WhiteSmallishText(), "party-search");
        }
        SingleComposer = composer.EndChildElements().Compose();
        var search = SingleComposer.GetTextInput("search");
        search?.SetValue(controller.Session.Current.Search, false);
        search?.SetPlaceHolderText(PlayerMenuShell.T(Inventory == null ? "search" : "item-search"));
        SingleComposer.GetTextInput("party-search")?.SetValue(controller.Party?.State.Search ?? "", false);
        SingleComposer.GetTextInput("party-search")?.SetPlaceHolderText(PlayerMenuShell.T("party-search-hint"));
        if (returning)
        {
            var input = SingleComposer.GetTextInput("search");
            if (restoreInventorySearchFocus && input != null) SingleComposer.FocusElement(input.TabIndex);
        }
    }
    private void OnIntent(string intent)
    {
        var session = controller.Session;
        if (intent == "party:search")
        {
            var input = SingleComposer?.GetTextInput("party-search");
            if (input != null) SingleComposer?.FocusElement(input.TabIndex);
            return;
        }
        if (intent == "party") { controller.OpenParty(); Compose(); return; }
        if (intent.StartsWith("party:", StringComparison.Ordinal))
        { controller.Party?.Handle(intent[6..]); Compose(); return; }
        if (intent is "focus-search" or "search")
        {
            var input = SingleComposer?.GetTextInput("search");
            if (input != null) SingleComposer?.FocusElement(input.TabIndex);
            return;
        }
        if (intent == "close") { TryClose(); return; }
        if (intent == "hub:retry") { controller.Hub?.RequestSnapshot(); Compose(); return; }
        if (Inventory != null && session.Page == PlayerMenuPage.Inventory && HandleInventoryIntent(intent)) { Compose(); return; }
        if (intent == "back") { if (controller.Party?.State.IsOpen != true) session.Update(session.Current with { Focus = "" }); controller.Back(layout?.AlternatePanes == true); if (!session.IsOpen) { TryClose(); return; } }
        else if (intent.StartsWith("page:", StringComparison.Ordinal))
        {
            if (controller.Party?.State.IsOpen == true && controller.Party.Close() is { } context) session.Update(context);
            controller.Navigate(Enum.Parse<PlayerMenuPage>(intent[5..]));
        }
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
        var page = Inventory!; var session = controller.Session; double now = capi.World.ElapsedMilliseconds;
        var query = page.Query;
        if (intent.StartsWith("entry:", StringComparison.Ordinal))
        {
            page.Select(intent[6..]);
            session.Update(session.Current with { SelectedEntry = page.View.SelectedId, InspectorOnly = layout?.AlternatePanes == true, InspectorScroll = 0 });
        }
        else if (intent == "equip") page.TrySubmitAction(VRPG.Network.PlayerItemAction.Equip, now);
        else if (intent == "unequip") page.TrySubmitAction(VRPG.Network.PlayerItemAction.Unequip, now);
        else if (intent == "select-page") { if (!page.View.Stale && !page.View.Loading) page.View.Selection.SelectPage(page.View.Entries); }
        else if (intent == "clear-selection") page.View.Selection.Clear();
        else if (intent == "retry-detail") page.Select(page.View.SelectedId);
        else if (intent == "refresh") page.Refresh(now);
        else if (intent == "next-page") { page.NextPage(now); session.Update(session.Current with { ScrollAnchor = 0 }); }
        else if (intent == "previous-page") { page.PreviousPage(now); session.Update(session.Current with { ScrollAnchor = 0 }); }
        else if (intent == "view") { page.View.Grid = !page.View.Grid; session.Update(session.Current with { ScrollAnchor = 0 }); }
        else if (intent == "equipment-next")
            controller.ChangeInventoryQuery(page.Query with { EquipmentOffset = checked(page.Query.EquipmentOffset + 64) }, now);
        else if (intent == "equipment-previous")
            controller.ChangeInventoryQuery(page.Query with { EquipmentOffset = Math.Max(0, page.Query.EquipmentOffset - 64) }, now);
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
            controller.ChangeInventoryQuery(query, now);
        }
        return true;
    }
    public override void OnMouseMove(MouseEvent args)
    {
        if (shell?.TalentDragging == true) shell.OnMouseMove(capi, args);
        else base.OnMouseMove(args);
    }
    public override void OnMouseUp(MouseEvent args)
    {
        // The dialog owns capture so releases outside the graph cannot become a click later.
        if (shell?.TalentDragging == true) shell.OnMouseUpOnElement(capi, args);
        else base.OnMouseUp(args);
    }
    public override void OnKeyDown(KeyEvent args)
    {
        var textField = SingleComposer?.GetTextInput("party-search")?.HasFocus == true ? SingleComposer.GetTextInput("party-search") : SingleComposer?.GetTextInput("search");
        bool typing = textField?.HasFocus == true;
        if (args.KeyCode == (int)GlKeys.Escape)
        {
            if (typing) { textField?.OnFocusLost(); args.Handled = true; return; }
            OnIntent("back"); args.Handled = true; return;
        }
        // Let the actual native text field consume letters before semantic menu actions.
        if (typing)
        {
            if (args.KeyCode == (int)GlKeys.Enter && SingleComposer?.GetTextInput("party-search")?.HasFocus == true)
            { controller.Party?.Handle("find"); shell?.Refresh(); }
            else if (args.KeyCode == (int)GlKeys.Tab)
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
        if (hubDirty) { hubDirty = false; shell?.Refresh(); }
        if (partyDirty)
        {
            partyDirty = false;
            if (composedPartyOpen != (controller.Party?.State.IsOpen == true)
                || composedPartySearch != (controller.Party?.ShowSearch(layout?.AlternatePanes == true) == true)) Compose();
            else shell?.Refresh();
        }
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
        if (controller.Hub != null) controller.Hub.Changed -= OnHubChanged;
        if (Inventory != null) Inventory.Changed -= OnInventoryChanged;
        if (controller.Party != null) { controller.Party.Changed -= OnPartyChanged; controller.Party.Dispose(); }
        base.Dispose();
    }
}
