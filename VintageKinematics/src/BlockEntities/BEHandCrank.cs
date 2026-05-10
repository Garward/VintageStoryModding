using Vintagestory.API.Client;
using Vintagestory.API.Common;
using VintageKinematics.Api;
using VintageKinematics.Rendering;

namespace VintageKinematics.BlockEntities
{
    public class BEHandCrank : BlockEntity
    {
        private KineticRotationRenderer renderer;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            if (api is not ICoreClientAPI capi) return;

            BEBehaviorKinetic beh = GetBehavior<BEBehaviorKinetic>();
            if (beh == null) return;

            capi.Tesselator.TesselateBlock(Block, out MeshData mesh);
            MeshRef meshRef = capi.Render.UploadMesh(mesh);
            renderer = new KineticRotationRenderer(capi, Pos, beh, meshRef);
            capi.Event.RegisterRenderer(renderer, EnumRenderStage.Opaque);
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            renderer?.Dispose();
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            renderer?.Dispose();
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            return true;
        }
    }
}
