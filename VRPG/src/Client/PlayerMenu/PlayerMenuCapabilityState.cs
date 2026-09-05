using VRPG.Network;

namespace VRPG.Client.PlayerMenu;

/// <summary>Truthful readiness advertised by the server for the current connection.</summary>
public readonly record struct PlayerMenuCapabilityState(
    bool InventoryReplacementEnabled,
    bool ItemStoreReady,
    bool DialogReplacementReady)
{
    public static PlayerMenuCapabilityState Unavailable => new(false, false, false);

    public static PlayerMenuCapabilityState FromPacket(PlayerMenuCapabilityPacket packet)
    {
        bool protocolMatches = packet.ProtocolVersion == PlayerMenuCapabilityProtocol.CurrentVersion;
        bool storeReady = protocolMatches
            && packet.InventoryReplacementEnabled
            && packet.ItemStoreReady;
        return new PlayerMenuCapabilityState(
            InventoryReplacementEnabled: protocolMatches && packet.InventoryReplacementEnabled,
            ItemStoreReady: storeReady,
            DialogReplacementReady: storeReady && packet.DialogReplacementReady);
    }
}
