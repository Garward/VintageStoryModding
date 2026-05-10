using System.Collections.Generic;
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
                    rotators.Add(new KineticAnimatorRenderer.Rotator
                    {
                        ElementName = elem,
                        Axis = axis,
                        Ratio = r["ratio"].AsFloat(1f),
                        PhaseOffset = r["phaseOffset"].AsFloat(0f)
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
