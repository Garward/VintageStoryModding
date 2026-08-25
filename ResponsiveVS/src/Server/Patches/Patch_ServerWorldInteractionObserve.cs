using ResponsiveVS.Diagnostics;
using Vintagestory.API.Common;
using Vintagestory.Common;
using Vintagestory.Server;

namespace ResponsiveVS.Server.Patches;

public static class Patch_ServerWorldInteractionObserve
{
    public static void InventoryHandInteractionPrefix(Packet_Client packet, ConnectedClient client)
    {
        if (!ResponsiveDiagnostics.BasicEnabled)
        {
            return;
        }

        long id = ResponsiveDiagnostics.NextEventId();
        ResponsiveDiagnostics.Basic(
            "WORLD SERVER hand-recv-item #{0} player={1} {2}",
            id,
            client?.Player?.PlayerName,
            WorldInteractionDiagFormat.HandPacket(packet?.HandInteraction));
    }

    public static void InventoryHandInteractionPostfix(Packet_Client packet, ConnectedClient client)
    {
        if (!ResponsiveDiagnostics.BasicEnabled)
        {
            return;
        }

        ResponsiveDiagnostics.Basic(
            "WORLD SERVER hand-done-item player={0} state={1} serverUsing={2} handUse={3}",
            client?.Player?.PlayerName,
            packet?.HandInteraction != null ? ((EnumHandInteractNw)packet.HandInteraction.EnumHandInteract).ToString() : "none",
            client?.Player?.Entity?.Controls?.UsingCount ?? -1,
            client?.Player?.Entity?.Controls?.HandUse);
    }

    public static void BlockHandInteractionPrefix(Packet_Client packet, ConnectedClient client)
    {
        if (!ResponsiveDiagnostics.BasicEnabled)
        {
            return;
        }

        long id = ResponsiveDiagnostics.NextEventId();
        ResponsiveDiagnostics.Basic(
            "WORLD SERVER hand-recv-block #{0} player={1} {2}",
            id,
            client?.Player?.PlayerName,
            WorldInteractionDiagFormat.HandPacket(packet?.HandInteraction));
    }

    public static void BlockHandInteractionPostfix(Packet_Client packet, ConnectedClient client)
    {
        if (!ResponsiveDiagnostics.BasicEnabled)
        {
            return;
        }

        ResponsiveDiagnostics.Basic(
            "WORLD SERVER hand-done-block player={0} state={1} serverUsing={2} handUse={3}",
            client?.Player?.PlayerName,
            packet?.HandInteraction != null ? ((EnumHandInteractNw)packet.HandInteraction.EnumHandInteract).ToString() : "none",
            client?.Player?.Entity?.Controls?.UsingCount ?? -1,
            client?.Player?.Entity?.Controls?.HandUse);
    }
}
