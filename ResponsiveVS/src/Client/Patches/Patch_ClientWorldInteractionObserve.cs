using ResponsiveVS.Diagnostics;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;

namespace ResponsiveVS.Client.Patches;

public static class Patch_ClientWorldInteractionObserve
{
    public static void SendHandInteractionPrefix(
        ClientMain __instance,
        int mouseButton,
        BlockSelection blockSel,
        EntitySelection entitySel,
        EnumHandInteract useType,
        EnumHandInteractNw state,
        bool firstEvent,
        EnumItemUseCancelReason cancelReason)
    {
        if (!ResponsiveDiagnostics.BasicEnabled)
        {
            return;
        }

        long id = ResponsiveDiagnostics.NextEventId();
        ResponsiveDiagnostics.Basic(
            "WORLD CLIENT hand-send #{0} {1}",
            id,
            WorldInteractionDiagFormat.ClientSend(
                mouseButton,
                blockSel,
                entitySel,
                useType,
                state,
                firstEvent,
                cancelReason,
                __instance?.EntityPlayer));
    }
}
