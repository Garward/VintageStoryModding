using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using VintageKinematics.Rendering;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Client-side linked pleat motion for bellows/accordion-like parts. Unlike independent
    /// rotators, every strip in a chain shares solved joint coordinates between fixed bottom
    /// and moving top anchors.
    /// </summary>
    public class BEBehaviorKineticLinkedPleat : BlockEntityBehavior
    {
        private KineticLinkedPleatRenderer renderer;
        private List<KineticLinkedPleatRenderer.Chain> chains;

        public IEnumerable<string> ManagedElementNames
        {
            get
            {
                foreach (var chain in chains)
                {
                    foreach (var pleat in chain.Pleats) yield return pleat.ElementName;
                }
            }
        }

        public BEBehaviorKineticLinkedPleat(BlockEntity blockentity) : base(blockentity) { }

        public override void Initialize(ICoreAPI api, JsonObject properties)
        {
            base.Initialize(api, properties);
            chains = new List<KineticLinkedPleatRenderer.Chain>();

            var arr = properties?["chains"];
            if (arr == null || !arr.Exists) arr = Block?.Attributes?["vkKineticLinkedPleat"]?["chains"];
            if (arr != null && arr.Exists)
            {
                foreach (var c in arr.AsArray())
                {
                    var chain = new KineticLinkedPleatRenderer.Chain
                    {
                        Plane = c["plane"].AsString("xy"),
                        Waveform = ParseWaveform(c["waveform"].AsString("sine")),
                        Ratio = c["ratio"].AsFloat(1f),
                        PhaseOffset = c["phaseOffset"].AsFloat(0f),
                        Invert = c["invert"].AsBool(false),
                        Bottom = ParseVec(c["bottom"], new Vec3f(0f, 0f, 8f)),
                        Top = ParseVec(c["top"], new Vec3f(0f, 8f, 8f)),
                        TopTravelY = c["topTravelY"].AsFloat(0f),
                        XA = c["xA"].AsFloat(0f),
                        XB = c["xB"].AsFloat(1f),
                        ZA = c["zA"].AsFloat(0f),
                        ZB = c["zB"].AsFloat(1f),
                        StartAtA = c["startAtA"].AsBool(true),
                        TranslateOnly = c["translateOnly"].AsBool(false),
                        TranslateTOffset = c["translateTOffset"].AsFloat(-1f),
                        TranslateTStep = c["translateTStep"].AsFloat(0f)
                    };

                    var elements = c["elements"];
                    if (elements != null && elements.Exists)
                    {
                        foreach (var elem in elements.AsArray())
                        {
                            string name = elem.AsString();
                            if (!string.IsNullOrEmpty(name))
                            {
                                chain.Pleats.Add(new KineticLinkedPleatRenderer.Pleat { ElementName = name });
                            }
                        }
                    }

                    if (chain.Pleats.Count > 0) chains.Add(chain);
                }
            }

            if (api is ICoreClientAPI capi && chains.Count > 0)
            {
                foreach (var chain in chains)
                {
                    foreach (var pleat in chain.Pleats)
                    {
                        var (mesh, pivot) = KineticMeshSplitter.TesselateElement(capi, Block, pleat.ElementName);
                        pleat.Mesh = mesh;
                        pleat.Pivot = pivot;
                        pleat.BaseLength = KineticMeshSplitter.GetCanonicalElementLength(capi, Block, pleat.ElementName, IsYZPlane(chain.Plane) ? "z" : "x");
                    }
                }

                BEBehaviorKinetic kineticBeh = Blockentity.GetBehavior<BEBehaviorKinetic>();
                renderer = new KineticLinkedPleatRenderer(capi, Pos, Block, kineticBeh, chains);
                capi.Event.RegisterRenderer(renderer, EnumRenderStage.Opaque);
            }
        }

        private static KineticPistonRenderer.EnumPistonWaveform ParseWaveform(string value)
        {
            System.Enum.TryParse(value, true, out KineticPistonRenderer.EnumPistonWaveform wave);
            return wave;
        }

        private static bool IsYZPlane(string plane)
        {
            return string.Equals(plane, "zy", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(plane, "yz", System.StringComparison.OrdinalIgnoreCase);
        }

        private static Vec3f ParseVec(JsonObject obj, Vec3f fallback)
        {
            if (obj == null || !obj.Exists) return fallback;
            return new Vec3f(
                obj["x"].AsFloat(fallback.X),
                obj["y"].AsFloat(fallback.Y),
                obj["z"].AsFloat(fallback.Z)
            );
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
                foreach (var chain in chains)
                {
                    foreach (var pleat in chain.Pleats) pleat.Mesh?.Dispose();
                }
                renderer.Dispose();
                renderer = null;
            }
        }
    }
}
