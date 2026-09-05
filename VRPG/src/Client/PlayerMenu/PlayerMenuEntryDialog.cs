using System;
using Vintagestory.API.Client;

namespace VRPG.Client.PlayerMenu;

/// <summary>
/// Connects an existing semantic menu action to the unified player-menu controller.
/// It deliberately owns no composition or player state.
/// </summary>
public sealed class PlayerMenuEntryDialog : GuiDialog
{
    private readonly string menuAction;
    private readonly PlayerMenuEntryPoint entryPoint;
    private readonly Action<PlayerMenuEntryPoint> activate;

    public PlayerMenuEntryDialog(
        ICoreClientAPI api,
        string menuAction,
        PlayerMenuEntryPoint entryPoint,
        Action<PlayerMenuEntryPoint> activate)
        : base(api)
    {
        this.menuAction = menuAction;
        this.entryPoint = entryPoint;
        this.activate = activate;
    }

    public override string ToggleKeyCombinationCode => menuAction;

    public override bool Focusable => false;

    public override void Toggle()
    {
        activate(entryPoint);
    }

    public override bool TryOpen()
    {
        activate(entryPoint);
        return true;
    }

    public override bool TryOpen(bool withFocus)
    {
        activate(entryPoint);
        return true;
    }
}
