using System;
using VRPG.Inventory;
using VRPG.Inventory.Migration;
using VRPG.Network;
using Vintagestory.API.Config;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.Server;

namespace VRPG.Modules.Rpg;

public sealed partial class RpgModule
{
    private bool AdventureEnabled => AdventureProfile.IsEnabled(config.Enabled, config.InventoryReplacement.Enabled);
    private bool MenuServiceReady => gearRuntime.IsReady && playerItems != null;
    private bool AdventurePlayerReady(IServerPlayer player) => AdventureEnabled
        && adventureProfile()?.CanParticipate(player.PlayerUID, MenuServiceReady) == true;

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

        RefuseAdventureParticipation(player, error);
        return false;
    }

    private void RefuseAdventureParticipation(IServerPlayer player, string error)
    {
        playerItems?.Disconnect(player);
        serverApi?.Logger.Error("[VRPG/Adventure] Admission refused for {0}: {1}", player.PlayerUID, error);
        // Revoke readiness even if failure happened after establishing the session.
        try { SendPlayerMenuCapabilities(player, refused: true); }
        finally
        {
            string message = Lang.Get("vrpg:adventure-unavailable");
            // Installed 1.22.7 Disconnect(string) passes its text as the OTHER players' message.
            // Supply the native departing-client argument explicitly; do not announce diagnostics.
            if (serverApi?.World is ServerMain server
                && server.Clients.TryGetValue(player.ClientId, out var client) && ReferenceEquals(client.Player, player))
                server.DisconnectPlayer(client, othersKickmessage: null, hisKickMessage: message);
            else
            {
                try { player.SendMessage(GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification); }
                finally { player.Disconnect(); }
            }
        }
    }

    private void SendPlayerMenuCapabilities(IServerPlayer player) => SendPlayerMenuCapabilities(player, refused: false);

    private void SendPlayerMenuCapabilities(IServerPlayer player, bool refused)
    {
        bool ready = !refused && AdventurePlayerReady(player);
        channel.SendPacket(new PlayerMenuCapabilityPacket
        {
            ProtocolVersion = PlayerMenuCapabilityProtocol.CurrentVersion,
            InventoryReplacementEnabled = AdventureEnabled,
            ItemStoreReady = ready,
            DialogReplacementReady = ready
        }, player);
    }
}
