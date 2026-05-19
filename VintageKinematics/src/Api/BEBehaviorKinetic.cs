using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using VintageKinematics.Network;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Core kinetic-network membership behavior. Attach to any BlockEntity that
    /// participates in a kinetic network — the network manager discovers it,
    /// builds connectivity, and pushes RPM / stress / conflict state back here.
    /// Sources additionally need <see cref="BEBehaviorKineticSource"/>.
    /// </summary>
    public class BEBehaviorKinetic : BlockEntityBehavior
    {
        /// <summary>Shaft axis this block presents to the network.</summary>
        public EnumKineticAxis Axis { get; set; }
        /// <summary>Structural role; see <see cref="EnumKineticRole"/>.</summary>
        public EnumKineticRole Role { get; set; }
        /// <summary>Gear ratio relative to the network source. Source itself is 1.0.</summary>
        public float Ratio { get; set; } = 1f;
        /// <summary>Rotation direction relative to source: +1 same, -1 reversed.</summary>
        public int Direction { get; set; } = 1;
        /// <summary>Visual phase offset (radians) used by renderers.</summary>
        public float PhaseOffset { get; set; }
        /// <summary>Id of the network this node currently belongs to.</summary>
        public long NetworkId { get; set; }
        /// <summary>Su contribution. Positive = consumer, negative = source.</summary>
        public float StressImpact { get; set; }
        /// <summary>Signed RPM at this position; sign matches network direction.</summary>
        public float CurrentRPM { get; set; }
        /// <summary>Cached client-side flag that the network is conflicted.</summary>
        public bool NetworkConflicted { get; set; }
        // Cached network-wide stress totals, pushed from the server whenever the network
        // recomputes. Lets the client tooltip display authoritative numbers without needing
        // its own copy of the network manager's state.
        /// <summary>Cached client-side stress total pushed from the server.</summary>
        public float NetStressTotal { get; set; }
        /// <summary>Cached client-side stress capacity pushed from the server.</summary>
        public float NetStressCapacity { get; set; }
        /// <summary>Cached client-side overstressed flag pushed from the server.</summary>
        public bool NetOverstressed { get; set; }
        /// <summary>Cached client-side node count pushed from the server.</summary>
        public int NetNodeCount { get; set; }
        /// <summary>Read-only view of the network this block belongs to. Null on client when the manager doesn't track it.</summary>
        public IKineticNetworkInfo Network
        {
            get
            {
                var mgr = Api?.ModLoader.GetModSystem<KineticNetworkManager>();
                return mgr?.GetNetwork(NetworkId);
            }
        }

        /// <summary>
        /// Network view suitable for tooltips: returns the live network on the server, or
        /// a snapshot built from cached server-pushed fields on the client when the manager
        /// doesn't track networks locally.
        /// </summary>
        public IKineticNetworkInfo EffectiveNetwork => Network ?? new CachedNetworkView(this);

        private sealed class CachedNetworkView : IKineticNetworkInfo
        {
            private readonly BEBehaviorKinetic beh;
            public CachedNetworkView(BEBehaviorKinetic beh) { this.beh = beh; }
            public long NetworkId      => beh.NetworkId;
            public float SourceRPM     => beh.CurrentRPM;
            public float MaxRPM        => KineticNetwork.MaxAbsRPMFallback;
            public bool IsConflicted   => beh.NetworkConflicted;
            public bool IsOverstressed => beh.NetOverstressed;
            public float StressTotal   => beh.NetStressTotal;
            public float StressCapacity => beh.NetStressCapacity;
            public int NodeCount       => beh.NetNodeCount;
        }

        /// <summary>Effective signed RPM after conflict/overstress gating. Zero when the network is halted.</summary>
        public float ActualRPM
        {
            get
            {
                if (NetworkConflicted) return 0f;
                if (Api?.Side == EnumAppSide.Server)
                {
                    var net = Network;
                    if (net != null && !net.IsConflicted) return net.SourceRPM * Ratio * Direction;
                }
                return CurrentRPM;
            }
        }

        /// <summary>True if the network is conflicted (server-authoritative when available, cached client value otherwise).</summary>
        public bool IsConflicted
        {
            get
            {
                if (Api?.Side == EnumAppSide.Server)
                {
                    var n = Network;
                    if (n != null) return n.IsConflicted;
                }
                return NetworkConflicted;
            }
        }

        /// <summary>Fired whenever <see cref="CurrentRPM"/> is recomputed (server propagation).</summary>
        public event Action<float> OnRPMChanged;

        internal void RaiseRPMChanged(float newRPM) => OnRPMChanged?.Invoke(newRPM);

        /// <summary>Standard BlockEntityBehavior constructor.</summary>
        public BEBehaviorKinetic(BlockEntity blockentity) : base(blockentity) { }

        public override void Initialize(ICoreAPI api, JsonObject properties)
        {
            base.Initialize(api, properties);

            bool axisExplicit = false;
            if (properties != null)
            {
                if (properties["role"].Exists)
                {
                    string roleStr = properties["role"].AsString("Shaft");
                    if (Enum.TryParse(roleStr, true, out EnumKineticRole parsed))
                        Role = parsed;
                }
                StressImpact = properties["stressImpact"].AsFloat(0f);
                if (StressImpact < 0f)
                {
                    var cfg = api.ModLoader.GetModSystem<KineticConfigSystem>()?.Config;
                    if (cfg != null)
                    {
                        float mult = cfg.ResolveGeneratorStress(Block?.Code?.FirstCodePart());
                        if (mult > 0f) StressImpact *= mult;
                    }
                }
                if (properties["axis"].Exists)
                {
                    string axisStr = properties["axis"].AsString();
                    if (Enum.TryParse(axisStr, true, out EnumKineticAxis explicitAxis))
                    {
                        Axis = explicitAxis;
                        axisExplicit = true;
                    }
                }
            }

            // EnumKineticAxis.X is 0, which equals default(EnumKineticAxis). Track explicit
            // assignment separately — testing `Axis == default` would silently overwrite a
            // valid X declaration with the AxisFromBlockFacing fallback.
            if (!axisExplicit)
            {
                Axis = AxisFromBlockFacing();
            }

            // Networks live in RAM on the manager; chunk load / world load doesn't restore them.
            // Ask the manager to ensure we're tracked once Initialize and FromTreeAttributes have
            // both settled. OnBlockPlaced (fresh placement) already calls OnPlaced separately —
            // EnsureTrackedFromLoad is a no-op when the pos is already tracked, so this is safe.
            if (api.Side == EnumAppSide.Server)
            {
                api.Event.RegisterCallback(_ =>
                {
                    var mgr = api.ModLoader.GetModSystem<KineticNetworkManager>();
                    mgr?.EnsureTrackedFromLoad(Pos);
                }, 0);
            }
        }

        private EnumKineticAxis AxisFromBlockFacing()
        {
            string axisVariant = Block.Variant["axis"];
            if (axisVariant == "x") return EnumKineticAxis.X;
            if (axisVariant == "y") return EnumKineticAxis.Y;
            if (axisVariant == "z") return EnumKineticAxis.Z;
            return EnumKineticAxis.Y;
        }

        public override void OnBlockPlaced(ItemStack byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);
            if (Api.Side != EnumAppSide.Server) return;

            var mgr = Api.ModLoader.GetModSystem<KineticNetworkManager>();
            mgr?.OnPlaced(Pos);
        }

        public override void OnBlockBroken(IPlayer byPlayer = null)
        {
            if (Api?.Side == EnumAppSide.Server)
            {
                var mgr = Api.ModLoader.GetModSystem<KineticNetworkManager>();
                mgr?.OnRemoved(Pos);
            }
            base.OnBlockBroken(byPlayer);
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetInt("axis", (int)Axis);
            tree.SetInt("role", (int)Role);
            tree.SetFloat("ratio", Ratio);
            tree.SetInt("direction", Direction);
            tree.SetFloat("phaseOffset", PhaseOffset);
            tree.SetLong("networkId", NetworkId);
            tree.SetFloat("stressImpact", StressImpact);
            tree.SetFloat("currentRPM", CurrentRPM);
            tree.SetBool("netConflicted", NetworkConflicted);
            tree.SetFloat("netStressTotal", NetStressTotal);
            tree.SetFloat("netStressCapacity", NetStressCapacity);
            tree.SetBool("netOverstressed", NetOverstressed);
            tree.SetInt("netNodeCount", NetNodeCount);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            Axis = (EnumKineticAxis)tree.GetInt("axis", (int)Axis);
            // Role and StressImpact are STRUCTURAL — they come from the
            // blocktype JSON via Initialize and must not be overridden by saved state.
            // Reading them from the tree pins legacy worlds to whatever values were current
            // when the block was first placed, defeating any later JSON change.
            Ratio = tree.GetFloat("ratio", 1f);
            Direction = tree.GetInt("direction", 1);
            PhaseOffset = tree.GetFloat("phaseOffset", 0f);
            NetworkId = tree.GetLong("networkId", 0);
            CurrentRPM = tree.GetFloat("currentRPM", 0f);
            NetworkConflicted = tree.GetBool("netConflicted", false);
            NetStressTotal = tree.GetFloat("netStressTotal", 0f);
            NetStressCapacity = tree.GetFloat("netStressCapacity", 0f);
            NetOverstressed = tree.GetBool("netOverstressed", false);
            NetNodeCount = tree.GetInt("netNodeCount", 0);
        }

        /// <summary>Snapshots this behavior into an internal <see cref="KineticNode"/> for the network builder.</summary>
        public KineticNode ToNode()
        {
            // Source rating, when present, lets the network compute a stable per-source
            // capacity that doesn't drift as the network's effective RPM changes.
            float ratedRPM = 0f;
            if (StressImpact < 0f)
            {
                var src = Blockentity?.GetBehavior<BEBehaviorKineticSource>();
                if (src != null) ratedRPM = src.IsActive ? src.TargetRPM : 0f;
            }
            return new KineticNode
            {
                Pos = Pos,
                Axis = Axis,
                Role = Role,
                Ratio = Ratio,
                Direction = Direction,
                PhaseOffset = PhaseOffset,
                StressImpact = StressImpact,
                RoundConsumerStressUp = ShouldRoundConsumerStressUp(),
                RatedRPM = ratedRPM,
                NetworkId = NetworkId
            };
        }

        private bool ShouldRoundConsumerStressUp()
        {
            string path = Block?.Code?.Path;
            return StressImpact > 0f && path != null && path.StartsWith("belt-", StringComparison.Ordinal);
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            var net = EffectiveNetwork;
            KineticTooltipBuilder.AppendStatus(dsc, net, ActualRPM);
            KineticTooltipBuilder.AppendStress(dsc, net);

            float absRPM = MathF.Abs(ActualRPM);
            if (StressImpact > 0f)
            {
                float contribution = StressImpact * absRPM;
                if (ShouldRoundConsumerStressUp()) contribution = MathF.Ceiling(contribution);
                dsc.AppendLine($"This block: +{contribution:F0} Su consumer (@ {absRPM:F0} RPM)");
            }
            else if (StressImpact < 0f)
            {
                var src = Blockentity?.GetBehavior<BEBehaviorKineticSource>();
                float ratedRPM = src?.TargetRPM ?? 0f;
                bool active = src != null && src.IsActive;
                float capacity = active ? -StressImpact * ratedRPM : 0f;
                string state = active ? "active" : "idle";
                dsc.AppendLine($"This block: {capacity:F0} Su source ({state}, rated {ratedRPM:F0} RPM)");
            }
        }
    }
}
