using System.Collections.Generic;
using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using VintageKinematics.Rendering;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Client-side per-element rotation. Reads <c>rotators</c> from the JSON
    /// behavior properties (each entry naming a shape element, axis, ratio,
    /// and phaseOffset) and exposes <see cref="GetCurrentAngleFor"/> for
    /// shape mesh assembly.
    /// </summary>
    public class BEBehaviorKineticAnimator : BlockEntityBehavior
    {
        private KineticAnimatorRenderer renderer;
        private List<KineticAnimatorRenderer.Rotator> rotators;

        /// <summary>Element names this behavior animates; consumed by <see cref="KineticMeshSplitter.CollectManagedElements"/>.</summary>
        public IEnumerable<string> ManagedElementNames
        {
            get { foreach (var r in rotators) yield return r.ElementName; }
        }

        /// <summary>Standard BlockEntityBehavior constructor.</summary>
        public BEBehaviorKineticAnimator(BlockEntity be) : base(be) { }

        public override void Initialize(ICoreAPI api, JsonObject properties)
        {
            base.Initialize(api, properties);
            rotators = new List<KineticAnimatorRenderer.Rotator>();

            var arr = properties?["rotators"];
            if (arr != null && arr.Exists)
            {
                foreach (var r in arr.AsArray())
                {
                    string elem = r["element"].AsString();
                    if (string.IsNullOrEmpty(elem)) continue;
                    string axisStr = r["axis"].AsString("Y");
                    System.Enum.TryParse(axisStr, true, out EnumKineticAxis axis);

                    string translateAxisStr = r["translateAxis"].AsString(axisStr);
                    System.Enum.TryParse(translateAxisStr, true, out EnumKineticAxis translateAxis);

                    string modeStr = r["mode"].AsString("spin");
                    System.Enum.TryParse(modeStr, true, out KineticAnimatorRenderer.EnumRotatorMode mode);

                    string waveStr = r["waveform"].AsString("sine");
                    System.Enum.TryParse(waveStr, true, out KineticPistonRenderer.EnumPistonWaveform wave);

                    rotators.Add(new KineticAnimatorRenderer.Rotator
                    {
                        ElementName = elem,
                        Axis = axis,
                        Mode = mode,
                        Waveform = wave,
                        Ratio = r["ratio"].AsFloat(1f),
                        PhaseOffset = r["phaseOffset"].AsFloat(0f),
                        MinAngle = DegToRad(r["minAngle"].AsFloat(0f)),
                        MaxAngle = DegToRad(r["maxAngle"].AsFloat(0f)),
                        Invert = r["invert"].AsBool(false),
                        AlignToKineticAxis = r["alignToKineticAxis"].AsBool(mode == KineticAnimatorRenderer.EnumRotatorMode.Spin),
                        TranslateAxis = translateAxis,
                        TranslateTravel = r["translateTravel"].AsFloat(0f),
                        TranslatePhaseOffset = r["translatePhaseOffset"].AsFloat(0f),
                        TranslateInvert = r["translateInvert"].AsBool(false)
                    });
                }
            }

            if (api is ICoreClientAPI capi && rotators.Count > 0)
            {
                foreach (var r in rotators)
                {
                    var (mesh, pivot) = KineticMeshSplitter.TesselateElement(capi, Block, r.ElementName);
                    r.Mesh = mesh;
                    r.Pivot = pivot;
                }

                var kineticBeh = Blockentity.GetBehavior<BEBehaviorKinetic>();
                renderer = new KineticAnimatorRenderer(capi, Pos, kineticBeh, rotators);
                capi.Event.RegisterRenderer(renderer, EnumRenderStage.Opaque);
            }
        }

        /// <summary>Current angle (radians) for the named shape element, or 0 if unknown.</summary>
        public float GetCurrentAngleFor(string elementName) => renderer?.ComputeAngleFor(elementName) ?? 0f;

        private static float DegToRad(float degrees) => degrees * MathF.PI / 180f;

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
                foreach (var r in rotators) r.Mesh?.Dispose();
                renderer.Dispose();
                renderer = null;
            }
        }
    }
}
