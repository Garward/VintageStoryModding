using System;
using VRPG.Client.PlayerMenu.Inventory;
using VRPG.Network;
using VRPG.Client.PlayerMenu.Party;
using VRPG.Client.PlayerMenu.Hub;

namespace VRPG.Client.PlayerMenu;

/// <summary>One shell controller reuses a connection-owned service across dialog lifetimes.</summary>
public sealed class PlayerMenuController
{
    private string worldIdentity = "";
    private readonly Func<double> nowMilliseconds;
    public PlayerMenuSessionState Session { get; } = new();
    public PlayerItemClientService Items { get; }
    public InventoryPage? Inventory { get; }
    public PartyPanel? Party { get; }
    public HubPageClientService? Hub { get; }
    public event Action? Changed;
    public PlayerMenuController(PlayerItemClientService connectionItems) { Items = connectionItems; nowMilliseconds = static () => 0; }
    public PlayerMenuController(PlayerItemClientService connectionItems, HubPageClientService hub) : this(connectionItems) { Hub = hub; }
    public PlayerMenuController(InventoryPage inventory, Func<double> worldClock, HubPageClientService hub) : this(inventory, worldClock) { Hub = hub; }
    public PlayerMenuController(InventoryPage inventory, Func<double> worldClock, HubPageClientService hub, PartyPanel party)
        : this(inventory, worldClock, hub) { Party = party ?? throw new ArgumentNullException(nameof(party)); }
    public PlayerMenuController(PlayerItemClientService connectionItems, PartyPanel party) : this(connectionItems) { Party = party; }
    public PlayerMenuController(InventoryPage inventory, PartyPanel party) : this(inventory) { Party = party; }
    public void OpenParty()
    {
        if (Party == null) return;
        if (!Session.IsOpen) Session.Enter(PlayerMenuEntryPoint.InventoryMenu);
        Party.Open(Session.Current); Session.Update(Session.Current with { Focus = "party" }); Changed?.Invoke();
    }
    public PlayerMenuController(InventoryPage inventory) { Inventory = inventory; Items = inventory.Items; nowMilliseconds = static () => 0; }
    public PlayerMenuController(InventoryPage inventory, Func<double> worldClock) : this(inventory)
    { nowMilliseconds = worldClock ?? throw new ArgumentNullException(nameof(worldClock)); }
    public void Enter(PlayerMenuEntryPoint entry, bool textInputFocused = false)
    {
        if (textInputFocused) return;
        Session.Enter(entry);
        RequestPageSnapshot();
        if (!Session.IsOpen) { Party?.Close(); Items.CloseMenu(); }
        else if (Inventory != null && entry == PlayerMenuEntryPoint.CharacterMenu)
            ChangeInventoryQuery(Inventory.Query with { Category = VRPG.Inventory.PlayerItemCategories.Gear,
                Location = VRPG.Inventory.PlayerItemLocation.Stored, Offset = 0, EquipmentOffset = 0 }, nowMilliseconds());
        Changed?.Invoke();
    }
    public void Close() { Party?.Close(); Session.Close(); Items.CloseMenu(); Changed?.Invoke(); }
    public void Navigate(PlayerMenuPage page)
    {
        if (!Enum.IsDefined(page)) return;
        if (Party?.State.IsOpen == true && Party.Close() is { } context) Session.Update(context);
        Session.Navigate(page); RequestPageSnapshot(); Changed?.Invoke();
    }
    private void RequestPageSnapshot()
    {
        if (Session.IsOpen && Session.Page != PlayerMenuPage.Inventory && Hub?.HasSnapshot == false) Hub.RequestSnapshot();
    }
    public void Back(bool alternatePanes = false)
    {
        if (Party?.State.IsOpen == true)
        {
            if (!Party.Back() && Party.Close() is { } context) Session.Update(context);
        }
        else { Session.Back(alternatePanes); if (!Session.IsOpen) Items.CloseMenu(); }
        Changed?.Invoke();
    }
    public void SetWorld(string identity)
    {
        if (worldIdentity == identity) return;
        worldIdentity = identity;
        ResetRetainedWorldState();
        Items.ResetConnection();
        Changed?.Invoke();
    }
    /// <summary>Resets every world-scoped menu state once from the lifecycle owner.</summary>
    public void ResetForWorldChange()
    {
        worldIdentity = "";
        ResetRetainedWorldState();
        Items.ResetConnection();
        Changed?.Invoke();
    }
    public void ResetConnection(string identity)
    {
        worldIdentity = identity;
        ResetRetainedWorldState();
        Items.ResetConnection();
        Changed?.Invoke();
    }
    private void ResetRetainedWorldState()
    {
        Hub?.ResetConnection();
        Inventory?.ResetForWorld();
        Session.ResetForWorldChange();
    }
    public void QueueInventoryQuery(double nowMilliseconds) => Items.QueueQuery(new PlayerItemQueryRequest
    { Category = Session.Current.Category, Search = Session.Current.Search, Offset = Session.Current.ScrollAnchor, Limit = 64 }, nowMilliseconds);
    public void ChangeInventoryQuery(InventoryQueryState query, double nowMilliseconds)
    {
        if (Inventory == null) return;
        bool sameItems = Inventory.Query.HasSameItems(query);
        Inventory.ChangeQuery(query, nowMilliseconds);
        if (!sameItems) Session.Update(Session.Current with { Category = query.Category, Search = query.Search,
            ScrollAnchor = 0, SelectedEntry = "", InspectorScroll = 0 });
        Changed?.Invoke();
    }
    public void Select(string entryId)
    {
        Session.Update(Session.Current with { SelectedEntry = entryId });
        Items.Select(entryId); Changed?.Invoke();
    }
}
