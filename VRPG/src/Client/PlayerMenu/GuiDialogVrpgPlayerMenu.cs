using System;
using VRPG.Client.PlayerMenu.Shell;
using VRPG.Client.PlayerMenu.Components;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VRPG.Client.PlayerMenu;

/// <summary>Single native composition host; gallery mode is explicitly separate from future production page adapters.</summary>
public sealed class GuiDialogVrpgPlayerMenu : GuiDialog
{
    private readonly PlayerMenuController controller;
    private readonly PlayerMenuGallery gallery;
    private PlayerMenuShell? shell;
    private PlayerMenuLayout? layout;
    public override string? ToggleKeyCombinationCode => null;
    public GuiDialogVrpgPlayerMenu(ICoreClientAPI api, PlayerMenuController controller, PlayerMenuGallery gallery) : base(api)
    { this.controller = controller; this.gallery = gallery; }
    public override void OnGuiOpened()
    {
        if (!controller.Session.IsOpen) controller.Enter(PlayerMenuEntryPoint.InventoryMenu);
        Compose(); base.OnGuiOpened();
    }
    public override void OnGuiClosed() { controller.Close(); base.OnGuiClosed(); }
    private void Compose()
    {
        layout = PlayerMenuLayout.ForViewport(capi.Render.FrameWidth, capi.Render.FrameHeight, RuntimeEnv.GUIScale);
        gallery.Equipment.SetExpanded(controller.Session.Current.EquipmentExpanded, controller.Session.ReducedMotion);
        var bounds = ElementBounds.Fixed(0, 0, layout.Width, layout.Height);
        shell = new(capi, bounds, layout, controller.Session, gallery, OnIntent);
        SingleComposer?.Dispose();
        var composer = capi.Gui.CreateCompo("vrpg-player-menu-gallery", ElementStdBounds.AutosizedMainDialog)
            .BeginChildElements(ElementBounds.Fixed(0, 0, layout.Width, layout.Height))
            .AddInteractiveElement(shell, "shell");
        if (!layout.AlternatePanes || !controller.Session.Current.InspectorOnly)
        {
            var searchBounds = layout.BrowserSearch;
            composer.AddTextInput(ElementBounds.Fixed(searchBounds.X, searchBounds.Y, searchBounds.Width, searchBounds.Height), value =>
            {
                var input = new SearchField(); input.SetText(value ?? "");
                controller.Session.Update(controller.Session.Current with { Search = input.Text, ScrollAnchor = 0 });
                shell?.Refresh();
            }, CairoFont.WhiteSmallishText(), "search");
        }
        SingleComposer = composer.EndChildElements().Compose();
        var search = SingleComposer.GetTextInput("search");
        search?.SetValue(controller.Session.Current.Search, false);
        search?.SetPlaceHolderText(PlayerMenuShell.T("search"));
    }
    private void OnIntent(string intent)
    {
        var session = controller.Session;
        if (intent is "focus-search" or "search")
        {
            var input = SingleComposer?.GetTextInput("search");
            if (input != null) SingleComposer?.FocusElement(input.TabIndex);
            return;
        }
        if (intent == "close") { TryClose(); return; }
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
        else if (intent == "state") gallery.CycleState();
        else if (intent == "count") { gallery.CycleCount(); session.Update(session.Current with { ScrollAnchor = 0 }); }
        else if (intent == "labels") gallery.ToggleLabels();
        else if (intent == "motion") session.ReducedMotion = !session.ReducedMotion;
        else if (intent == "action") gallery.Activate();
        Compose();
    }
    public override void OnKeyDown(KeyEvent args)
    {
        bool typing = SingleComposer?.GetTextInput("search")?.HasFocus == true;
        if (args.KeyCode == (int)GlKeys.Escape)
        {
            if (typing) { SingleComposer?.GetTextInput("search")?.OnFocusLost(); args.Handled = true; return; }
            OnIntent("back"); args.Handled = true; return;
        }
        // Let the actual native text field consume letters before semantic menu actions.
        if (typing)
        {
            if (args.KeyCode == (int)GlKeys.Tab)
            {
                SingleComposer?.UnfocusOwnElements(); shell?.AdvanceFocus(args.ShiftPressed);
            }
            else SingleComposer?.GetTextInput("search")?.OnKeyDown(capi, args);
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
        var next = PlayerMenuLayout.ForViewport(capi.Render.FrameWidth, capi.Render.FrameHeight, RuntimeEnv.GUIScale);
        if (layout != next) Compose();
        base.OnRenderGUI(deltaTime);
    }
}
