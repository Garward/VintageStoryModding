using System;
using VRPG.Network;
using Vintagestory.API.Client;

namespace VRPG.Client.PlayerMenu;

/// <summary>Connection lifecycle for the two semantic entries into the production player menu.</summary>
public sealed class PlayerMenuReplacement : IDisposable
{
    private readonly PlayerMenuController controller;
    private readonly GuiDialogVrpgPlayerMenu menu;
    private readonly PlayerMenuEntryDialog inventoryEntry, characterEntry;
    private readonly PlayerMenuReplacementCoordinator<GuiDialog> coordinator;
    private readonly IClientEventAPI events;
    private long restorationTickId;
    private PlayerMenuCapabilityState capability;
    private bool disposed;
    public PlayerMenuReplacement(ICoreClientAPI api, PlayerMenuController controller, GuiDialogVrpgPlayerMenu menu)
    {
        this.controller = controller; this.menu = menu;
        events = api.Event;
        inventoryEntry = new(api, VanillaMenuActions.Inventory, PlayerMenuEntryPoint.InventoryMenu, Enter);
        characterEntry = new(api, VanillaMenuActions.Character, PlayerMenuEntryPoint.CharacterMenu, Enter);
        coordinator = new(api.Gui.LoadedGuis, inventoryEntry, characterEntry, d => d.ToggleKeyCombinationCode,
            d => !d.IsOpened() || d.TryClose(), d => d.OnBlockTexturesLoaded());
        controller.Items.ConnectionChanged += OnConnectionChanged;
    }
    public bool IsInstalled => coordinator.IsInstalled;
    public bool RestorationPending => coordinator.RestorationPending;
    public Exception? LastFailure { get; private set; }
    public void Receive(PlayerMenuCapabilityPacket packet)
    {
        if (disposed) return;
        capability = PlayerMenuCapabilityState.FromPacket(packet);
        if (!capability.DialogReplacementReady) Restore();
        else Poll();
    }
    // Native dialogs can register after the capability packet; retry on the client game thread.
    public void Poll()
    {
        if (disposed && !RestorationPending) return;
        coordinator.TryInstall(!disposed && capability.DialogReplacementReady
            && controller.Items.ConnectionGeneration > 0 && controller.Inventory != null);
        LastFailure = coordinator.LastFailure;
        UpdateRestorationRetry();
    }
    private void Enter(PlayerMenuEntryPoint entry)
    {
        if (disposed || !IsInstalled) return;
        controller.Enter(entry);
        if (controller.Session.IsOpen)
        {
            if (!menu.TryOpen()) controller.Close();
        }
        else menu.TryClose();
    }
    private void OnConnectionChanged()
    {
        if (controller.Items.ConnectionGeneration == 0)
        {
            capability = PlayerMenuCapabilityState.Unavailable;
            Restore();
        }
    }
    private void Restore()
    {
        LastFailure = null;
        Attempt(() => { if (menu.IsOpened()) menu.TryClose(); });
        Attempt(controller.Close);
        Attempt(coordinator.Restore);
        UpdateRestorationRetry();
    }
    private void Attempt(Action action)
    {
        try { action(); }
        catch (Exception failure) { LastFailure = LastFailure == null ? failure : new AggregateException(LastFailure, failure); }
    }
    private void UpdateRestorationRetry()
    {
        if (RestorationPending && restorationTickId == 0)
            restorationTickId = events.RegisterGameTickListener(_ => Poll(), 25);
        else if (!RestorationPending && restorationTickId != 0)
        {
            events.UnregisterGameTickListener(restorationTickId);
            restorationTickId = 0;
        }
    }
    public void LeaveWorld()
    {
        capability = PlayerMenuCapabilityState.Unavailable;
        Restore();
        controller.ResetForWorldChange();
    }
    public void Dispose()
    {
        if (disposed) { if (RestorationPending) Poll(); return; }
        disposed = true;
        capability = PlayerMenuCapabilityState.Unavailable;
        controller.Items.ConnectionChanged -= OnConnectionChanged;
        Restore();
        Attempt(inventoryEntry.Dispose);
        Attempt(characterEntry.Dispose);
    }
}
