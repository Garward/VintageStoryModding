using System;
using VRPG.Inventory;
using VRPG.Inventory.Migration;
using VRPG.Network;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace VRPG.Modules.Rpg;

public sealed partial class RpgModule
{
    private bool AdventureEnabled => AdventureProfile.IsEnabled(config.Enabled, config.InventoryReplacement.Enabled);
    private bool MenuServiceReady => gearRuntime.IsReady && playerItems != null;

    private void OnPlayerNowPlaying(IServerPlayer player)
    {
        if (!BeginAdventureSession(player)) return;
        resources?.OnPlayerNowPlaying(player);
        AdvanceOnboardingAfterAppearance(player);
        SendOnboardingState(player);
        NormalizeCharacterRuntime(player);
        SendLoadout(player);
        groundAreas?.SendSnapshot(player);
    }

    private bool BeginAdventureSession(IServerPlayer player)
    {
        if (!AdventureEnabled)
        {
            SendPlayerMenuCapabilities(player);
            return true;
        }
        string error;
        try
        {
            var result = adventureProfile()?.Prepare(new VintageStoryLegacyInventorySource(player), MenuServiceReady);
            if (result?.Success == true)
            {
                channel.SendPacket(playerItems!.Connect(player), player);
                SendPlayerMenuCapabilities(player);
                return true;
            }
            error = result?.Message ?? "Adventure profile is unavailable.";
        }
        catch (Exception exception) { error = exception.ToString(); }

        playerItems?.Disconnect(player);
        serverApi?.Logger.Error("[VRPG/Adventure] Admission refused for {0}: {1}", player.PlayerUID, error);
        // Revoke readiness even if failure happened after establishing the session.
        try { SendPlayerMenuCapabilities(player, refused: true); }
        finally { player.Disconnect(Lang.Get("vrpg:adventure-unavailable")); }
        return false;
    }

    private void SendPlayerMenuCapabilities(IServerPlayer player) => SendPlayerMenuCapabilities(player, refused: false);

    private void SendPlayerMenuCapabilities(IServerPlayer player, bool refused)
    {
        bool ready = !refused && AdventureEnabled
            && adventureProfile()?.CanParticipate(player.PlayerUID, MenuServiceReady) == true;
        channel.SendPacket(new PlayerMenuCapabilityPacket
        {
            ProtocolVersion = PlayerMenuCapabilityProtocol.CurrentVersion,
            InventoryReplacementEnabled = AdventureEnabled,
            ItemStoreReady = ready,
            DialogReplacementReady = ready
        }, player);
    }
}
