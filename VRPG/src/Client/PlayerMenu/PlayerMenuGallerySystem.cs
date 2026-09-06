using System;
using VRPG.Client.PlayerMenu.Inventory;
using VRPG.Client.PlayerMenu.Shell;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VRPG.Client.PlayerMenu;

/// <summary>Opt-in developer preview. Never replaces Vanilla dialogs or attaches a network service.</summary>
public sealed class PlayerMenuGallerySystem : ModSystem
{
    private GuiDialogVrpgPlayerMenu? dialog;
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client
        && Environment.GetEnvironmentVariable("VRPG_PLAYER_MENU_GALLERY") == "1";
    public override void StartClientSide(ICoreClientAPI api)
    {
        api.ChatCommands.Create("vrpgmenugallery")
            .WithDescription("Open the isolated developer player-menu component gallery.")
            .HandleWith(_ =>
            {
                dialog ??= new(api, new PlayerMenuController(new PlayerItemClientService()), new PlayerMenuGallery());
                dialog.TryOpen();
                return TextCommandResult.Success(PlayerMenuShell.T("gallery-isolated"));
            });
    }
    public override void Dispose() { dialog?.Dispose(); dialog = null; base.Dispose(); }
}
