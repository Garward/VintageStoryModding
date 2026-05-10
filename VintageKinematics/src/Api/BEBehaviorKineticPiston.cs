using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using VintageKinematics.Rendering;

namespace VintageKinematics.Api
{
    /// <summary>
    /// JSON-driven piston behavior. Reads <c>pistons</c> array from properties; each entry
    /// names a shape element, axis, mode (oscillate or directional), travel (1/16ths),
    /// ratio, optional phaseOffset/waveform, and optional <c>invert</c> (negates the offset for
    /// either mode — useful for downward-extending pistons whose axis is Y).
    /// Exposes <see cref="GetOffsetFor"/> for BE authors to translate elements each frame.
    /// Directional positions persist across save/load and tick on both server and client
    /// so visuals stay in sync with authoritative save state.
    /// </summary>
    public class BEBehaviorKineticPiston : BlockEntityBehavior
    {
        private KineticPistonRenderer renderer;
        private List<KineticPistonRenderer.Piston> pistons;

        private struct PhaseCrossSubscription
        {
            public string ElementName;
            public float Threshold;
            public Action Callback;
        }
        private readonly List<PhaseCrossSubscription> phaseCrossSubs = new List<PhaseCrossSubscription>();
        private readonly Dictionary<string, float> lastPhasePerElement = new Dictionary<string, float>();

        /// <summary>Element names this behavior translates; consumed by <see cref="KineticMeshSplitter.CollectManagedElements"/>.</summary>
        public IEnumerable<string> ManagedElementNames
        {
            get { foreach (var p in pistons) yield return p.ElementName; }
        }

        /// <summary>Standard BlockEntityBehavior constructor.</summary>
        public BEBehaviorKineticPiston(BlockEntity be) : base(be) { }

        public override void Initialize(ICoreAPI api, JsonObject properties)
        {
            base.Initialize(api, properties);
            pistons = new List<KineticPistonRenderer.Piston>();

            var arr = properties?["pistons"];
            if (arr != null && arr.Exists)
            {
                foreach (var p in arr.AsArray())
                {
                    string elem = p["element"].AsString();
                    if (string.IsNullOrEmpty(elem)) continue;

                    string axisStr = p["axis"].AsString("Y");
                    System.Enum.TryParse(axisStr, true, out EnumKineticAxis axis);

                    string modeStr = p["mode"].AsString("oscillate");
                    System.Enum.TryParse(modeStr, true, out KineticPistonRenderer.EnumPistonMode mode);

                    string waveStr = p["waveform"].AsString("sine");
                    System.Enum.TryParse(waveStr, true, out KineticPistonRenderer.EnumPistonWaveform wave);

                    pistons.Add(new KineticPistonRenderer.Piston
                    {
                        ElementName = elem,
                        Axis = axis,
                        Mode = mode,
                        Waveform = wave,
                        Travel = p["travel"].AsFloat(6f),
                        Ratio = p["ratio"].AsFloat(1f),
                        PhaseOffset = p["phaseOffset"].AsFloat(0f),
                        Invert = p["invert"].AsBool(false),
                        CurrentPos = 0f
                    });
                }
            }

            var kineticBeh = Blockentity.GetBehavior<BEBehaviorKinetic>();
            if (kineticBeh == null)
            {
                api.Logger.Warning($"[VintageKinematics] BEBehaviorKineticPiston at {Pos} has no sibling Kinetic behavior - inert");
                return;
            }

            if (api is ICoreClientAPI capi && pistons.Count > 0)
            {
                foreach (var p in pistons)
                {
                    var (mesh, _) = KineticMeshSplitter.TesselateElement(capi, Block, p.ElementName);
                    p.Mesh = mesh;
                }

                renderer = new KineticPistonRenderer(capi, Pos, kineticBeh, pistons);
                capi.Event.RegisterRenderer(renderer, EnumRenderStage.Opaque);
            }

            if (pistons.Count > 0)
            {
                Blockentity.RegisterGameTickListener(dt => OnTick(dt, kineticBeh), 50);
            }
        }

        private void OnTick(float dt, BEBehaviorKinetic kin)
        {
            float rpm = kin.ActualRPM;
            foreach (var p in pistons)
            {
                if (p.Mode != KineticPistonRenderer.EnumPistonMode.Directional) continue;
                p.CurrentPos = KineticPistonMath.AdvanceDirectional(p.CurrentPos, rpm, p.Ratio, dt, p.Travel, p.Invert);
            }

            DispatchPhaseCrossings(kin);
        }

        private void DispatchPhaseCrossings(BEBehaviorKinetic kin)
        {
            if (phaseCrossSubs.Count == 0) return;

            foreach (var p in pistons)
            {
                if (p.Mode != KineticPistonRenderer.EnumPistonMode.Oscillate) continue;

                bool subscribed = false;
                for (int i = 0; i < phaseCrossSubs.Count; i++)
                {
                    if (phaseCrossSubs[i].ElementName == p.ElementName) { subscribed = true; break; }
                }
                if (!subscribed) continue;

                float phase = ComputeOscillatePhase(p, kin);
                if (lastPhasePerElement.TryGetValue(p.ElementName, out float last))
                {
                    for (int i = 0; i < phaseCrossSubs.Count; i++)
                    {
                        var sub = phaseCrossSubs[i];
                        if (sub.ElementName != p.ElementName) continue;
                        if (CrossedThreshold(last, phase, sub.Threshold))
                        {
                            try { sub.Callback?.Invoke(); }
                            catch (Exception ex) { Api?.Logger.Error($"[VintageKinematics] Phase-cross callback threw at {Pos}: {ex}"); }
                        }
                    }
                }

                lastPhasePerElement[p.ElementName] = phase;
            }
        }

        private float ComputeOscillatePhase(KineticPistonRenderer.Piston p, BEBehaviorKinetic kin)
        {
            float t = (float)(Api?.World?.ElapsedMilliseconds ?? 0) / 1000f;
            float rpm = kin?.ActualRPM ?? 0f;
            float baseAngle = 2f * MathF.PI * MathF.Abs(rpm) * p.Ratio / 60f * t
                              + (kin?.PhaseOffset ?? 0f) + p.PhaseOffset;
            float twoPi = 2f * MathF.PI;
            return baseAngle - twoPi * MathF.Floor(baseAngle / twoPi);
        }

        // Threshold ∈ [0, 2π). Phase is monotonically increasing modulo 2π. Detect a forward
        // crossing of the threshold over the interval (last, current]. Handles 2π wrap.
        private static bool CrossedThreshold(float last, float current, float threshold)
        {
            if (last <= current)
                return last < threshold && threshold <= current;
            // Wrapped through 2π
            return threshold > last || threshold <= current;
        }

        /// <summary>
        /// Subscribe to a phase landmark on a specific oscillating piston element. The callback
        /// fires once each time the wave's phase advances past <paramref name="thresholdRadians"/>
        /// (forward crossings only). Useful landmarks: <c>π</c> = max extension (bottom-out for
        /// inverted Y pistons), <c>0</c> = rest after the return stroke, <c>π/2</c> = max outward
        /// velocity, <c>3π/2</c> = max inward velocity.
        ///
        /// The handler runs on whichever side this BlockEntity is on — register on both sides if
        /// you need separate behavior (e.g. visuals on client, inventory on server). Subscriptions
        /// live as long as the BlockEntity; no manual unsubscribe is required.
        ///
        /// No-ops if the named element doesn't exist or isn't an oscillate-mode piston.
        /// </summary>
        public void OnPhaseCross(string elementName, float thresholdRadians, Action callback)
        {
            if (string.IsNullOrEmpty(elementName) || callback == null) return;
            phaseCrossSubs.Add(new PhaseCrossSubscription
            {
                ElementName = elementName,
                Threshold = thresholdRadians,
                Callback = callback
            });
        }

        /// <summary>Current displacement vector (1/16ths) for the named element, or zero if unknown.</summary>
        public Vec3f GetOffsetFor(string elementName) => renderer?.ComputeOffsetFor(elementName) ?? new Vec3f();

        /// <summary>
        /// Current oscillation phase (radians, modulo 2π) for the named oscillating piston element.
        /// Returns 0 for directional pistons (which don't oscillate) and for unknown element names.
        /// Server-callable: enables event-driven logic that fires on wave landmarks (e.g. detecting
        /// bottom-out for impact effects). The wave's maximum extension occurs at phase π.
        /// </summary>
        public float GetCurrentPhaseFor(string elementName)
        {
            if (pistons == null) return 0f;
            var kineticBeh = Blockentity.GetBehavior<BEBehaviorKinetic>();
            float t = (float)(Api?.World?.ElapsedMilliseconds ?? 0) / 1000f;
            float rpm = kineticBeh?.ActualRPM ?? 0f;
            foreach (var p in pistons)
            {
                if (p.ElementName != elementName) continue;
                if (p.Mode != KineticPistonRenderer.EnumPistonMode.Oscillate) return 0f;
                float baseAngle = 2f * MathF.PI * MathF.Abs(rpm) * p.Ratio / 60f * t
                                  + (kineticBeh?.PhaseOffset ?? 0f) + p.PhaseOffset;
                float twoPi = 2f * MathF.PI;
                float wrapped = baseAngle - twoPi * MathF.Floor(baseAngle / twoPi);
                return wrapped;
            }
            return 0f;
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            if (pistons == null) return;
            for (int i = 0; i < pistons.Count; i++)
            {
                if (pistons[i].Mode == KineticPistonRenderer.EnumPistonMode.Directional)
                    tree.SetFloat($"pistonPos{i}", pistons[i].CurrentPos);
            }
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor world)
        {
            base.FromTreeAttributes(tree, world);
            if (pistons == null) return;
            for (int i = 0; i < pistons.Count; i++)
            {
                if (pistons[i].Mode == KineticPistonRenderer.EnumPistonMode.Directional)
                    pistons[i].CurrentPos = tree.GetFloat($"pistonPos{i}", 0f);
            }
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            DisposeRenderer();
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            DisposeRenderer();
        }

        private void DisposeRenderer()
        {
            if (Api is ICoreClientAPI capi && renderer != null)
            {
                capi.Event.UnregisterRenderer(renderer, EnumRenderStage.Opaque);
                foreach (var p in pistons) p.Mesh?.Dispose();
                renderer.Dispose();
                renderer = null;
            }
        }
    }
}
