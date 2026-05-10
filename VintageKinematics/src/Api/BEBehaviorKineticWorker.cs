using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace VintageKinematics.Api
{
    /// <summary>Pure work-cycle math; testable without a BlockEntity.</summary>
    public class KineticWorkAccumulator
    {
        /// <summary>Threshold at which <see cref="OnCycle"/> fires.</summary>
        public float WorkPerCycle { get; }
        /// <summary>Below this absolute RPM, no progress accumulates.</summary>
        public float MinRPM { get; }
        /// <summary>If true, progress resets to 0 on each cycle (overflow discarded).</summary>
        public bool AutoReset { get; }
        /// <summary>Current accumulated progress.</summary>
        public float CurrentProgress { get; private set; }

        /// <summary>Fires when accumulated progress crosses <see cref="WorkPerCycle"/>.</summary>
        public event Action OnCycle;
        /// <summary>Fires every tick that adds progress. (current, total)</summary>
        public event Action<float, float> OnProgress;

        /// <summary>Constructs an accumulator. <paramref name="autoReset"/> defaults true.</summary>
        public KineticWorkAccumulator(float workPerCycle, float minRPM, bool autoReset = true)
        {
            WorkPerCycle = workPerCycle;
            MinRPM = minRPM;
            AutoReset = autoReset;
        }

        /// <summary>Advance progress by <c>|rpm|·dt</c>; no-op if <paramref name="gated"/> or <c>|rpm|</c> &lt; MinRPM.</summary>
        public void Tick(float rpm, float dt, bool gated)
        {
            if (gated) return;
            float absRpm = MathF.Abs(rpm);
            if (absRpm < MinRPM) return;

            CurrentProgress += absRpm * dt;
            OnProgress?.Invoke(CurrentProgress, WorkPerCycle);

            while (CurrentProgress >= WorkPerCycle)
            {
                if (AutoReset) CurrentProgress = 0f;
                else CurrentProgress -= WorkPerCycle;
                OnCycle?.Invoke();
                if (AutoReset) break;
            }
        }

        /// <summary>Resets accumulated progress to zero.</summary>
        public void ResetProgress() { CurrentProgress = 0f; }

        /// <summary>Restores progress from saved state (clamped to >= 0).</summary>
        public void RestoreProgress(float p) { CurrentProgress = MathF.Max(0f, p); }
    }

    /// <summary>
    /// Recipe-agnostic work-cycle accumulator behavior. Drives a
    /// <see cref="KineticWorkAccumulator"/> from the sibling <see cref="BEBehaviorKinetic"/>'s
    /// RPM each tick, gated on conflict/overstress. Subscribe to <see cref="OnWorkCompleted"/>
    /// to do the actual work (e.g. consume input, produce output).
    /// </summary>
    public class BEBehaviorKineticWorker : BlockEntityBehavior
    {
        private KineticWorkAccumulator acc;
        private BEBehaviorKinetic parent;
        private float tickInterval = 0.25f;

        /// <summary>Progress accumulated toward the current work cycle.</summary>
        public float CurrentProgress => acc?.CurrentProgress ?? 0f;
        /// <summary>Threshold at which a work cycle fires.</summary>
        public float WorkPerCycle    => acc?.WorkPerCycle ?? 0f;

        /// <summary>Fires when a work cycle completes; subscribe to do the actual work.</summary>
        public event Action<KineticWorkCompletedArgs> OnWorkCompleted;
        /// <summary>Fires every tick that progress advances. (current, total)</summary>
        public event Action<float, float> OnWorkProgress;

        /// <summary>Standard BlockEntityBehavior constructor.</summary>
        public BEBehaviorKineticWorker(BlockEntity be) : base(be) { }

        public override void Initialize(ICoreAPI api, JsonObject properties)
        {
            base.Initialize(api, properties);

            float workPerCycle = properties?["workPerCycle"].AsFloat(100f) ?? 100f;
            float minRPM       = properties?["minRPM"].AsFloat(8f) ?? 8f;
            bool autoReset     = properties?["autoReset"].AsBool(true) ?? true;
            tickInterval       = properties?["tickInterval"].AsFloat(0.25f) ?? 0.25f;

            var cfg = api.ModLoader.GetModSystem<KineticConfigSystem>()?.Config;
            if (cfg != null)
            {
                float speedMult = cfg.ResolveConsumerSpeed(Block?.Code?.FirstCodePart());
                if (speedMult > 0f) workPerCycle /= speedMult;
            }

            acc = new KineticWorkAccumulator(workPerCycle, minRPM, autoReset);
            acc.OnCycle    += FireCompleted;
            acc.OnProgress += FireProgress;

            parent = Blockentity.GetBehavior<BEBehaviorKinetic>();
            if (parent == null)
            {
                api.Logger.Warning($"[VintageKinematics] BEBehaviorKineticWorker at {Pos} has no sibling Kinetic behavior - inert");
                return;
            }

            if (api.Side == EnumAppSide.Server)
            {
                Blockentity.RegisterGameTickListener(OnTick, (int)(tickInterval * 1000f));
            }
        }

        /// <summary>Resets the worker's progress to zero.</summary>
        public void ResetProgress() => acc?.ResetProgress();

        private void OnTick(float dt)
        {
            if (acc == null || parent == null) return;
            bool gated = parent.IsConflicted || (parent.Network?.IsOverstressed ?? false);
            try { acc.Tick(parent.CurrentRPM, dt, gated); }
            catch (Exception ex)
            {
                Api.Logger.Error($"[VintageKinematics] Worker handler threw at {Pos}: {ex}");
                acc.ResetProgress();
            }
        }

        private void FireCompleted()
        {
            var args = new KineticWorkCompletedArgs(Pos, parent.Network, parent.CurrentRPM);
            OnWorkCompleted?.Invoke(args);
        }

        private void FireProgress(float cur, float total) => OnWorkProgress?.Invoke(cur, total);

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetFloat("workerProgress", CurrentProgress);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor world)
        {
            base.FromTreeAttributes(tree, world);
            float saved = tree.GetFloat("workerProgress", 0f);
            acc?.RestoreProgress(saved);
        }
    }
}
