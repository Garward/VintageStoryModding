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
    private PlayerMenuCapabilityState capability;
    private bool disposed;
    public PlayerMenuReplacement(ICoreClientAPI api, PlayerMenuController controller, GuiDialogVrpgPlayerMenu menu)
    {
        this.controller = controller; this.menu = menu;
        inventoryEntry = new(api, VanillaMenuActions.Inventory, PlayerMenuEntryPoint.InventoryMenu, Enter);
        characterEntry = new(api, VanillaMenuActions.Character, PlayerMenuEntryPoint.CharacterMenu, Enter);
        coordinator = new(api.Gui.LoadedGuis, inventoryEntry, characterEntry, d => d.ToggleKeyCombinationCode,
            d => !d.IsOpened() || d.TryClose(), d => d.OnBlockTexturesLoaded());
        controller.Items.ConnectionChanged += OnConnectionChanged;
    }
    public bool IsInstalled => coordinator.IsInstalled;
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
        if (!disposed) coordinator.TryInstall(capability.DialogReplacementReady
            && controller.Items.ConnectionGeneration > 0 && controller.Inventory != null);
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
        try { if (menu.IsOpened()) menu.TryClose(); }
        finally
        {
            controller.Close();
            coordinator.Restore();
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
        if (disposed) return;
        Restore();
        disposed = true;
        controller.Items.ConnectionChanged -= OnConnectionChanged;
        inventoryEntry.Dispose(); characterEntry.Dispose();
    }
}
