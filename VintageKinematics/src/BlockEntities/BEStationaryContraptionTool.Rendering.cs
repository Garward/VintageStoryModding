using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.Rendering;

namespace VintageKinematics.BlockEntities
{
    public sealed partial class BEStationaryContraptionTool
    {
        private StationaryContraptionToolRenderer toolRenderer;
        private List<ContraptionMovingPartDefinition> movingParts;

        private void InitializeRenderer(ICoreClientAPI capi)
        {
            movingParts = new List<ContraptionMovingPartDefinition>();
            ContraptionMovingPartRegistry.CollectMovingParts(
                new ContraptionMovingPartContext(capi, null, Block, new Vec3i(), null, Pos.X, Pos.InternalY, Pos.Z),
                movingParts);
            movingParts.RemoveAll(part => part?.ElementNames == null || part.ElementNames.Length == 0);
            if (movingParts.Count == 0) return;

            toolRenderer = new StationaryContraptionToolRenderer(capi, Pos, Block, GetBehavior<BEBehaviorKinetic>(), movingParts);
            capi.Event.RegisterRenderer(toolRenderer, EnumRenderStage.Opaque);
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            if (movingParts == null || movingParts.Count == 0) return false;

            HashSet<string> names = new HashSet<string>();
            foreach (ContraptionMovingPartDefinition part in movingParts)
            foreach (string name in part.ElementNames)
            {
                if (!string.IsNullOrEmpty(name)) names.Add(name);
            }

            MeshData body = KineticMeshSplitter.TesselateBodyExcluding(
                Api as ICoreClientAPI,
                Block,
                tessThreadTesselator,
                new List<string>(names).ToArray());
            if (body != null) mesher.AddMeshData(body);
            return true;
        }

        private void DisposeRenderer()
        {
            if (Api is ICoreClientAPI capi && toolRenderer != null)
            {
                capi.Event.UnregisterRenderer(toolRenderer, EnumRenderStage.Opaque);
            }
            toolRenderer?.Dispose();
            toolRenderer = null;
        }

        public override void OnBlockUnloaded()
        {
            DisposeRenderer();
            base.OnBlockUnloaded();
        }

        public override void OnBlockRemoved()
        {
            DisposeRenderer();
            base.OnBlockRemoved();
        }
    }
}
