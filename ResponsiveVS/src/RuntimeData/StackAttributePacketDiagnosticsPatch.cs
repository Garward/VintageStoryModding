using ResponsiveVS.Config;
using Vintagestory.Common;

namespace ResponsiveVS.RuntimeData;

public static class StackAttributePacketDiagnosticsPatch
{
    public static void ToPacketPostfix(Packet_ItemStack __result)
    {
        RuntimeDataConfig config = ResponsiveVSConfigSystem.Config.RuntimeData;
        if (!config.EnableRuntimeDataHotPathPatch || !config.EnableStackAttributePacketDiagnostics || __result == null)
        {
            return;
        }

        RuntimeDataStats.RecordStackPacket(__result.Attributes?.Length ?? 0);
    }
}
