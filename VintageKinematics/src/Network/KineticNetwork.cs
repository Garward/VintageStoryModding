using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;

namespace VintageKinematics.Network
{
    public class KineticNetwork : IKineticNetworkInfo
    {
        // Overspeed cap: a node geared up past MaxAbsRPM cannot engage and stalls (matches
        // Create's behavior — running a chain too fast simply breaks). MinAbsRPM is the
        // source's minimum useful RPM; below this, a source is treated as off. It is NOT
        // applied per node — a cog geared down to 2 RPM should still spin at 2 RPM.
        public const float MinAbsRPM = 8f;
        public const float MaxAbsRPMFallback = 256f;

        public long NetworkId;
        public Dictionary<BlockPos, KineticNode> Nodes = new Dictionary<BlockPos, KineticNode>();

        public BlockPos SourcePos;             // null if no source
        public float SourceRPM;                // signed; sign determines whole-network direction
        public float MaxRPM = 256f;            // tier-derived per-network cap; fallback when no tiers declared
        public bool IsConflicted;
        public List<BlockPos> ConflictPositions = new List<BlockPos>();

        public bool IsOverstressed;            // total consumed stress > available capacity
        public float StressTotal;              // sum of consumer stress (RPM-scaled)
        public float StressCapacity;           // sum of source capacity (RPM-scaled)

        public int NodeCount { get { return Nodes.Count; } }

        // Explicit interface implementations forward fields to the IKineticNetworkInfo
        // contract since C# requires properties for interface members.
        long IKineticNetworkInfo.NetworkId       => NetworkId;
        float IKineticNetworkInfo.SourceRPM      => SourceRPM;
        float IKineticNetworkInfo.MaxRPM         => MaxRPM;
        bool IKineticNetworkInfo.IsConflicted    => IsConflicted;
        bool IKineticNetworkInfo.IsOverstressed  => IsOverstressed;
        float IKineticNetworkInfo.StressTotal    => StressTotal;
        float IKineticNetworkInfo.StressCapacity => StressCapacity;
        int IKineticNetworkInfo.NodeCount        => NodeCount;

        public float GetActualRPM(BlockPos pos)
        {
            if (IsConflicted || IsOverstressed) return 0f;
            if (!Nodes.TryGetValue(pos, out KineticNode node)) return 0f;
            return ApplyRPMCap(SourceRPM * node.Ratio * node.Direction);
        }

        public float ApplyRPMCap(float rpm)
        {
            float abs = MathF.Abs(rpm);
            if (abs < 0.0001f) return 0f;
            if (abs > MaxRPM) return 0f;
            return rpm;
        }

        public void ComputeMaxRPM()
        {
            int count = 0;
            float sum = 0f;
            foreach (var n in Nodes.Values)
            {
                if (string.IsNullOrEmpty(n.Tier) || n.TierMaxRPM <= 0f) continue;
                sum += n.TierMaxRPM;
                count++;
            }
            if (count == 0) { MaxRPM = MaxAbsRPMFallback; return; }
            float avg = sum / count;
            MaxRPM = MathF.Floor(avg / 8f) * 8f;
        }

        /// <summary>
        /// Recomputes stress totals.
        /// Consumers: contribute StressImpact × absRPM (linear). Sources: contribute a
        /// fixed capacity of -StressImpact × RatedRPM, the source's intrinsic rating, so the
        /// displayed capacity stays stable regardless of gear ratios. Capacity is zero when
        /// no source is currently driving the network.
        /// </summary>
        public void RecomputeStressForRPM(float sourceRPM)
        {
            StressTotal = 0f;
            StressCapacity = 0f;
            bool anySourceActive = MathF.Abs(sourceRPM) > 0.0001f;
            // Dedupe vanilla MP bridges by upstream networkId so multiple bridge nodes touching
            // the same vanilla MP network (e.g. player runs an axle chain past two kinetic shafts)
            // don't each contribute full capacity — that would multiply SU per connected axle.
            // Bridges to distinct vanilla networks still each count, since they're genuinely
            // independent power sources. Within each group we pick the bridge with the largest
            // capacity contribution; this protects against a transiently-stale bridge (e.g. one
            // whose vanilla BE was momentarily unloaded, leaving RatedRPM=0) winning by hash order
            // and zeroing out a network that actually has live power.
            Dictionary<long, float> bestBridgeCap = null;
            foreach (var n in Nodes.Values)
            {
                if (n.StressImpact > 0f)
                {
                    float nodeRPM = ApplyRPMCap(sourceRPM * n.Ratio * n.Direction);
                    float absRPM = MathF.Abs(nodeRPM);
                    if (absRPM < 0.0001f) continue;
                    StressTotal += n.StressImpact * absRPM;
                }
                else if (n.StressImpact < 0f && anySourceActive)
                {
                    if (n.IsVanillaBridge)
                    {
                        bestBridgeCap ??= new Dictionary<long, float>();
                        float cap = -n.StressImpact * n.RatedRPM;
                        bestBridgeCap.TryGetValue(n.VanillaNetworkId, out float prev);
                        if (cap > prev) bestBridgeCap[n.VanillaNetworkId] = cap;
                        continue;
                    }
                    StressCapacity += -n.StressImpact * n.RatedRPM;
                }
            }
            if (bestBridgeCap != null)
            {
                foreach (var cap in bestBridgeCap.Values) StressCapacity += cap;
            }
            IsOverstressed = StressTotal > StressCapacity + 0.0001f;
        }
    }
}
