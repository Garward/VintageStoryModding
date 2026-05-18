using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using VintageKinematics.Rendering;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Client-side element scaling for visual parts such as ropes, belts, or folds that need to
    /// grow or shrink while one end remains anchored. Supports sourceTimed progress from a sibling
    /// KineticSource and oscillating progress from a sibling Kinetic behavior.
    /// </summary>
    public class BEBehaviorKineticStretch : BlockEntityBehavior
    {
        private KineticStretchRenderer renderer;
        private List<KineticStretchRenderer.Stretch> stretches;

        public IEnumerable<string> ManagedElementNames
        {
            get { foreach (var s in stretches) yield return s.ElementName; }
        }

        public BEBehaviorKineticStretch(BlockEntity blockentity) : base(blockentity) { }

        public override void Initialize(ICoreAPI api, JsonObject properties)
        {
            base.Initialize(api, properties);
            stretches = new List<KineticStretchRenderer.Stretch>();

            var arr = properties?["stretches"];
            if (arr != null && arr.Exists)
            {
                foreach (var s in arr.AsArray())
                {
                    string elem = s["element"].AsString();
                    if (string.IsNullOrEmpty(elem)) continue;

                    string axisStr = s["axis"].AsString("Y");
                    System.Enum.TryParse(axisStr, true, out EnumKineticAxis axis);

                    string waveStr = s["waveform"].AsString("sine");
                    System.Enum.TryParse(waveStr, true, out KineticPistonRenderer.EnumPistonWaveform wave);

                    JsonObject pivotAttr = s["pivot"];
                    Vec3f pivot = new Vec3f(0.5f, 0.5f, 0.5f);
                    if (pivotAttr != null && pivotAttr.Exists)
                    {
                        pivot = new Vec3f(
                            pivotAttr["x"].AsFloat(8f) / 16f,
                            pivotAttr["y"].AsFloat(8f) / 16f,
                            pivotAttr["z"].AsFloat(8f) / 16f
                        );
                    }

                    stretches.Add(new KineticStretchRenderer.Stretch
                    {
                        ElementName = elem,
                        Axis = axis,
                        Mode = s["mode"].AsString("sourceTimed"),
                        Waveform = wave,
                        MinScale = s["minScale"].AsFloat(1f),
                        MaxScale = s["maxScale"].AsFloat(1f),
                        Ratio = s["ratio"].AsFloat(1f),
                        PhaseOffset = s["phaseOffset"].AsFloat(0f),
                        Invert = s["invert"].AsBool(false),
                        Pivot = pivot
                    });
                }
            }

            if (api is ICoreClientAPI capi && stretches.Count > 0)
            {
                foreach (var s in stretches)
                {
                    var (mesh, _) = KineticMeshSplitter.TesselateElement(capi, Block, s.ElementName);
                    s.Mesh = mesh;
                }

                BEBehaviorKinetic kineticBeh = Blockentity.GetBehavior<BEBehaviorKinetic>();
                BEBehaviorKineticSource sourceBeh = Blockentity.GetBehavior<BEBehaviorKineticSource>();
                renderer = new KineticStretchRenderer(capi, Pos, Block, kineticBeh, sourceBeh, stretches);
                capi.Event.RegisterRenderer(renderer, EnumRenderStage.Opaque);
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
                foreach (var s in stretches) s.Mesh?.Dispose();
                renderer.Dispose();
                renderer = null;
            }
        }
    }
}
