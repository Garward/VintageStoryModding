using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Network
{
    public static class NetworkBuilder
    {
        // 6 face neighbors + 12 axis-aligned diagonals = 18 candidates per node.
        private static readonly Vec3i[] Candidates = new Vec3i[]
        {
            // Face neighbors
            new Vec3i( 1, 0, 0), new Vec3i(-1, 0, 0),
            new Vec3i( 0, 1, 0), new Vec3i( 0,-1, 0),
            new Vec3i( 0, 0, 1), new Vec3i( 0, 0,-1),
            // Diagonal neighbors (axis-aligned 2-of-3)
            new Vec3i( 1, 1, 0), new Vec3i( 1,-1, 0),
            new Vec3i(-1, 1, 0), new Vec3i(-1,-1, 0),
            new Vec3i( 1, 0, 1), new Vec3i( 1, 0,-1),
            new Vec3i(-1, 0, 1), new Vec3i(-1, 0,-1),
            new Vec3i( 0, 1, 1), new Vec3i( 0, 1,-1),
            new Vec3i( 0,-1, 1), new Vec3i( 0,-1,-1),
        };

        public static KineticNetwork BuildFrom(INodeProvider prov, BlockPos start, long networkId)
        {
            var network = new KineticNetwork { NetworkId = networkId };

            if (!prov.TryGetNode(start, out KineticNode startNode))
            {
                return network;
            }

            // Seed: source node has ratio 1, direction 1, phase 0.
            startNode.Ratio = 1f;
            startNode.Direction = 1;
            startNode.PhaseOffset = 0f;
            startNode.NetworkId = networkId;
            network.Nodes[start] = startNode;

            var queue = new Queue<BlockPos>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                BlockPos curPos = queue.Dequeue();
                KineticNode curNode = network.Nodes[curPos];

                foreach (var off in Candidates)
                {
                    BlockPos nbrPos = new BlockPos(curPos.X + off.X, curPos.Y + off.Y, curPos.Z + off.Z, curPos.dimension);

                    if (!prov.TryGetNode(nbrPos, out KineticNode nbrNode)) continue;

                    var connOpt = KineticConnections.GetConnection(curNode, nbrNode);
                    if (!connOpt.HasValue)
                    {
                        connOpt = prov.TryGetCustomConnection(curNode, nbrNode);
                    }
                    if (!connOpt.HasValue) continue;
                    var conn = connOpt.Value;

                    float newRatio = curNode.Ratio * conn.Ratio;
                    int newDir = curNode.Direction * conn.Direction;
                    float newPhase = curNode.PhaseOffset + conn.PhaseOffset;

                    if (network.Nodes.TryGetValue(nbrPos, out KineticNode existing))
                    {
                        // Already visited via another path. Verify ratios agree.
                        const float Eps = 0.0001f;
                        bool ratioMatches = System.Math.Abs(existing.Ratio - newRatio) < Eps;
                        bool dirMatches = existing.Direction == newDir;
                        if (!ratioMatches || !dirMatches)
                        {
                            network.IsConflicted = true;
                            if (!network.ConflictPositions.Contains(nbrPos))
                                network.ConflictPositions.Add(nbrPos);
                        }
                        continue;
                    }

                    nbrNode.Ratio = newRatio;
                    nbrNode.Direction = newDir;
                    nbrNode.PhaseOffset = newPhase;
                    nbrNode.NetworkId = networkId;
                    network.Nodes[nbrPos] = nbrNode;
                    queue.Enqueue(nbrPos);
                }
            }

            NormalizeAgainstSource(network);
            return network;
        }

        // BFS seeds the start node at ratio 1, dir +1. When the start is downstream of the
        // actual source (e.g. a freshly placed cog joining an existing crank-driven chain),
        // the source ends with a fractional ratio and KineticNetwork.GetActualRPM mis-scales
        // SourceRPM — small ratios get clamped to zero by the per-node MinAbsRPM guard,
        // silently stopping cogs. Re-anchor here so any source (StressImpact < 0) is at
        // (ratio 1, dir +1) regardless of BFS start. If two sources disagree on the implied
        // anchor, flag the network as conflicted.
        private static void NormalizeAgainstSource(KineticNetwork network)
        {
            BlockPos anchorPos = null;
            float anchorRatio = 0f;
            int anchorDir = 0;
            foreach (var kvp in network.Nodes)
            {
                if (kvp.Value.StressImpact >= 0f) continue;
                if (anchorPos == null)
                {
                    anchorPos = kvp.Key;
                    anchorRatio = kvp.Value.Ratio;
                    anchorDir = kvp.Value.Direction;
                    continue;
                }
                // Second source: it must imply the same global anchor.
                float impliedRatio = kvp.Value.Ratio;
                int impliedDir = kvp.Value.Direction;
                bool ratioMatches = System.Math.Abs(impliedRatio - anchorRatio) < 0.0001f;
                bool dirMatches = impliedDir == anchorDir;
                if (!ratioMatches || !dirMatches)
                {
                    network.IsConflicted = true;
                    if (!network.ConflictPositions.Contains(kvp.Key))
                        network.ConflictPositions.Add(kvp.Key);
                }
            }

            if (anchorPos == null) return;
            if (anchorRatio <= 0f) return;

            float scale = 1f / anchorRatio;
            int sign = anchorDir;
            var keys = new List<BlockPos>(network.Nodes.Keys);
            foreach (var k in keys)
            {
                var n = network.Nodes[k];
                n.Ratio *= scale;
                n.Direction *= sign;
                network.Nodes[k] = n;
            }
        }
    }
}
