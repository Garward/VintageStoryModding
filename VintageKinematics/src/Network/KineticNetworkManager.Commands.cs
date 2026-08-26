using System;
using System.Text;
using Vintagestory.API.Common;

namespace VintageKinematics.Network
{
    public partial class KineticNetworkManager
    {
        private TextCommandResult HandleNetworkInfoCommand(TextCommandCallingArgs args)
        {
            bool verbose = string.Equals(args[0] as string, "verbose", StringComparison.OrdinalIgnoreCase);
            StringBuilder text = new();

            lock (lockObj)
            {
                int nodes = 0;
                int powered = 0;
                int conflicted = 0;
                int bridges = 0;

                foreach (KineticNetwork network in networks.Values)
                {
                    nodes += network.NodeCount;
                    if (Math.Abs(network.SourceRPM) > 0.01f) powered++;
                    if (network.IsConflicted || network.IsOverstressed) conflicted++;

                    foreach (KineticNode node in network.Nodes.Values)
                    {
                        if (node.IsVanillaBridge) bridges++;
                    }
                }

                text.Append($"Kinetic networks: {networks.Count}; nodes: {nodes}; powered: {powered}; troubled: {conflicted}; vanilla bridges: {bridges}.");
                if (!verbose)
                {
                    text.Append(" Use '/vk netinfo verbose' for every network.");
                    return TextCommandResult.Success(text.ToString());
                }

                foreach (KineticNetwork network in networks.Values)
                {
                    text.AppendLine();
                    text.Append($"#{network.NetworkId}: {network.NodeCount} nodes, source={network.SourceRPM:F1} RPM, stress={network.StressTotal:F0}/{network.StressCapacity:F0}, conflict={network.IsConflicted || network.IsOverstressed}");
                    foreach (var entry in network.Nodes)
                    {
                        KineticNode bridge = entry.Value;
                        if (!bridge.IsVanillaBridge) continue;

                        text.AppendLine();
                        text.Append($"  bridge @ {entry.Key.X},{entry.Key.Y},{entry.Key.Z}: vNet={bridge.VanillaNetworkId}, smTorque={bridge.SmoothedTorque:F3}, ratedRPM={bridge.RatedRPM:F1}, impact={bridge.StressImpact:F1}");
                    }
                }
            }

            return TextCommandResult.Success(text.ToString());
        }
    }
}
