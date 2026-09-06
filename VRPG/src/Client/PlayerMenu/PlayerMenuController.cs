using System;
using VRPG.Client.PlayerMenu.Inventory;
using VRPG.Network;

namespace VRPG.Client.PlayerMenu;

/// <summary>One shell controller reuses a connection-owned service across dialog lifetimes.</summary>
public sealed class PlayerMenuController
{
    private string worldIdentity = "";
    public PlayerMenuSessionState Session { get; } = new();
    public PlayerItemClientService Items { get; }
    public event Action? Changed;
    public PlayerMenuController(PlayerItemClientService connectionItems) { Items = connectionItems; }
    public void Enter(PlayerMenuEntryPoint entry, bool textInputFocused = false)
    {
        if (textInputFocused) return;
        Session.Enter(entry);
        if (!Session.IsOpen) Items.CloseMenu();
        Changed?.Invoke();
    }
    public void Close() { Session.Close(); Items.CloseMenu(); Changed?.Invoke(); }
    public void Back(bool alternatePanes = false) { Session.Back(alternatePanes); if (!Session.IsOpen) Items.CloseMenu(); Changed?.Invoke(); }
    public void SetWorld(string identity)
    {
        if (worldIdentity == identity) return;
        worldIdentity = identity; Items.ResetConnection(); Session.SetWorld(identity); Changed?.Invoke();
    }
    public void ResetConnection(string identity) { Items.ResetConnection(); SetWorld(identity); }
    public void QueueInventoryQuery(double nowMilliseconds) => Items.QueueQuery(new PlayerItemQueryRequest
    { Category = Session.Current.Category, Search = Session.Current.Search, Offset = Session.Current.ScrollAnchor, Limit = 64 }, nowMilliseconds);
    public void Select(string entryId)
    {
        Session.Update(Session.Current with { SelectedEntry = entryId });
        Items.Select(entryId); Changed?.Invoke();
    }
}
