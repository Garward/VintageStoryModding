using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using VintageKinematics.Network;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Marks a block as a power producer. Pair with <see cref="BEBehaviorKinetic"/>
    /// (which must declare <c>stressImpact &lt; 0</c>). Call <see cref="Wind"/>
    /// from your block's interaction handler to drive the network for a duration.
    /// </summary>
    public class BEBehaviorKineticSource : BlockEntityBehavior
    {
        /// <summary>RPM the source produces while active. Always non-negative; sign lives on <see cref="Direction"/>.</summary>
        public float TargetRPM { get; set; } = 8f;
        /// <summary>Seconds remaining of active drive. Zero means idle.</summary>
        public float DecaySeconds { get; set; } = 0f;
        /// <summary>If true, the source is permanently active at <see cref="TargetRPM"/> with no winding required.</summary>
        public bool AlwaysActive { get; set; } = false;
        /// <summary>+1 = clockwise (default), -1 = counter-clockwise. Multiplies TargetRPM when reporting to the network.</summary>
        public int Direction { get; set; } = 1;

        /// <summary>Signed output RPM. Keeps RatedRPM (magnitude) separate from rotation sign.</summary>
        public float SignedTargetRPM => Direction * TargetRPM;

        /// <summary>True when this source is currently driving the network (winding has time left, or AlwaysActive is set).</summary>
        public bool IsActive => AlwaysActive || DecaySeconds > 0f;

        /// <summary>Standard BlockEntityBehavior constructor.</summary>
        public BEBehaviorKineticSource(BlockEntity blockentity) : base(blockentity) { }

        public override void Initialize(ICoreAPI api, JsonObject properties)
        {
            base.Initialize(api, properties);
            if (properties != null)
            {
                TargetRPM = properties["targetRPM"].AsFloat(8f);
                AlwaysActive = properties["alwaysActive"].AsBool(false);
            }
            if (api.Side == EnumAppSide.Server)
            {
                if (AlwaysActive)
                {
                    api.Event.RegisterCallback(_ =>
                    {
                        var mgr = api.ModLoader.GetModSystem<KineticNetworkManager>();
                        mgr?.OnSourceChanged(Pos, SignedTargetRPM);
                    }, 0);
                }
                Blockentity.RegisterGameTickListener(OnTick, 1000);
            }
        }

        private void OnTick(float dt)
        {
            var mgr = Api.ModLoader.GetModSystem<KineticNetworkManager>();
            if (mgr == null) return;

            // OnSourceChanged is a silent no-op when no network exists for this pos. The 0ms
            // load-time callback that calls EnsureTrackedFromLoad can race with chunk-load
            // ordering and miss the source — retry here so the motor recovers within 1s.
            if (mgr.GetNetworkAt(Pos) == null) mgr.EnsureTrackedFromLoad(Pos);

            if (AlwaysActive)
            {
                mgr.OnSourceChanged(Pos, SignedTargetRPM);
                return;
            }

            if (DecaySeconds > 0f)
            {
                DecaySeconds -= dt;
                if (DecaySeconds < 0f) DecaySeconds = 0f;
                mgr.OnSourceChanged(Pos, DecaySeconds > 0f ? SignedTargetRPM : 0f);
            }
        }

        /// <summary>
        /// Activate the source for <paramref name="seconds"/> in the given <paramref name="direction"/>
        /// (+1 = clockwise, -1 = counter-clockwise). Re-winding with a different direction reverses
        /// the source immediately. The kinetic network picks up the new state on the next manager tick.
        /// </summary>
        public void Wind(float seconds = 5f, int direction = 1)
        {
            Direction = direction >= 0 ? 1 : -1;
            DecaySeconds = seconds;
            var mgr = Api.ModLoader.GetModSystem<KineticNetworkManager>();
            mgr?.OnSourceChanged(Pos, SignedTargetRPM);
            Blockentity.MarkDirty(true);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetFloat("targetRPM", TargetRPM);
            tree.SetFloat("decaySeconds", DecaySeconds);
            tree.SetInt("direction", Direction);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            TargetRPM = tree.GetFloat("targetRPM", 8f);
            DecaySeconds = tree.GetFloat("decaySeconds", 0f);
            Direction = tree.GetInt("direction", 1);
            if (Direction != -1) Direction = 1;
        }
    }
}
